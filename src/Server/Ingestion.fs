module Ingestion

open FSharp.Control.Tasks

type RefreshType = LatestMonth | Year of int
type RefreshResult = NothingToDo | Completed of {| Type : RefreshType; Rows : int; Hash : string |}

module LandRegistry =
    open Azure.Storage.Blobs
    open FSharp.Data
    open Microsoft.Extensions.Caching.Memory
    open System.Security.Cryptography
    open System
    open System.IO
    open System.Net
    open System.Threading.Tasks
    open System.Text
    open System.Text.Encodings.Web
    open System.Text.Json

    type PricePaid = CsvProvider<const (__SOURCE_DIRECTORY__ + "/price-paid-schema.csv"), PreferOptionals = true, Schema="Date=Date">
    type PostcodeResult = PostcodeResult of float * float
    type PricePaidAndGeo = { Property : PricePaid.Row; GeoLocation : PostcodeResult option }
    type HashedDownload = { Hash : string; Rows : PricePaidAndGeo array } 
    type ComparisonResult = FileAlreadyExists | NewFile of HashedDownload
    
    let md5 = MD5.Create()

    module Uris =
        let latestMonth = Uri "http://prod.publicdata.landregistry.gov.uk.s3-website-eu-west-1.amazonaws.com/pp-monthly-update-new-version.csv"
        let forYear year = Uri $"http://prod.publicdata.landregistry.gov.uk.s3-website-eu-west-1.amazonaws.com/pp-{year}.csv"

    let toJson =
        let options =
            JsonSerializerOptions (
                WriteIndented = false,
                IgnoreNullValues = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                PropertyNameCaseInsensitive = true)
        fun (value:obj) -> JsonSerializer.Serialize (value, options)
    let asHash (stream:Stream) =
        stream.Position <- 0
        stream
        |> md5.ComputeHash
        |> BitConverter.ToString
        |> String.filter ((<>) '-')

    module Exporters =
        // Export a row for JSON
        let asJsonOutput (row:PricePaidAndGeo) =
            let property, geo = row.Property, row.GeoLocation
            [
                "TransactionId", property.TransactionId |> string |> box
                "Price", property.Price |> box
                "DateOfTransfer", property.Date |> box
                "PostCode", property.Postcode |> Option.toObj |> box
                "PropertyType", property.PropertyType |> Option.toObj |> box
                "Build", property.Duration |> box
                "Contract", property.``Old/New`` |> box
                "Building", [ property.PAON; yield! Option.toList property.SAON ] |> String.concat " " |> box
                "Street", property.Street |> Option.toObj |> box
                "Locality", property.Locality |> Option.toObj |> box
                "Town", property.``Town/City`` |> box
                "District", property.District |> box
                "County", property.County |> box
                match geo with
                | Some (PostcodeResult (long, lat)) ->
                    "Geo",
                    [
                        "type", box "Point"
                        "coordinates", box [| long; lat |]
                    ]
                    |> readOnlyDict
                    |> box
                | None ->
                    ()
            ]
            |> List.filter (snd >> isNull >> not)
            |> readOnlyDict

        // Export a row for CSV
        let asCsvOutput (row:PricePaidAndGeo) =
            let property, geo = row.Property, row.GeoLocation
            [|
                property.TransactionId |> string
                property.Price |> string
                property.Date |> string
                property.Postcode |> Option.toObj |> string
                property.PropertyType |> Option.toObj |> string
                property.Duration |> string
                property.``Old/New`` |> string
                [ property.PAON; yield! Option.toList property.SAON ] |> String.concat " "
                property.Street |> Option.toObj |> string
                property.Locality |> Option.toObj |> string
                property.``Town/City`` |> string
                property.District |> string
                property.County |> string
                match geo with
                | Some (PostcodeResult (long, lat)) ->
                    [
                        "type", box "Point"
                        "coordinates", box [| long; lat |]
                    ]
                    |> readOnlyDict
                    |> toJson
                | None ->
                    ""
            |]
            |> Array.map (fun s -> s.Replace("\"", "\"\""))
            |> Array.map (fun s -> $"\"{s}\"")
            |> String.concat ","

        // Combine a chunk of JSON rows
        let processJsonChunk lines =
            lines
            |> Array.map toJson
            |> Array.toList

        // Combine a chunk of CSV rows
        let processCsvChunk lines =
            let header = [ "TransactionId";"Price";"DateOfTransfer";"PostCode";"PropertyType";"Build";"Contract";"Building";"Street";"Locality";"Town";"District";"County";"Geo" ] |> String.concat ","
            header :: Array.toList lines

        let Csv = (asCsvOutput, processCsvChunk), "csv"
        let Json = (asJsonOutput, processJsonChunk), "json"

    let toLongLat connectionString =
        let geoCache = new MemoryCache (MemoryCacheOptions())
        lazy (fun postcode ->
            geoCache.GetOrCreateAsync(
                postcode,
                fun (e:ICacheEntry) -> task {
                    match! GeoLookup.tryGetGeo connectionString postcode with
                    | Some geo -> return Some (geo.Long, geo.Lat)
                    | None -> return None
                }))

    let getPropertyStream requestType =
        let output = new MemoryStream ()
        use webClient = new WebClient ()
        let uri =
            match requestType with
            | LatestMonth -> Uris.latestMonth
            | Year year -> Uris.forYear year

        task {
            use! stream = webClient.OpenReadTaskAsync uri
            do! stream.CopyToAsync output
            return output
        }

    let tryGetLatestFile toLongLat (stream:Stream) (existingHashes:Set<_>) = task {
        let latestHash = stream |> asHash
        if existingHashes.Contains latestHash then
            return ComparisonResult.FileAlreadyExists
        else
            stream.Position <- 0L
            let noOp = Task.FromResult None
            let! encoded =
                let rows = PricePaid.Load stream
                rows.Rows
                |> Seq.toArray
                |> Array.map (fun (line) ->
                    task {
                        let! postcode =
                            match line.Postcode with
                            | None -> noOp
                            | Some postcode -> toLongLat postcode
                        return { Property = line; GeoLocation = postcode |> Option.map PostcodeResult }
                    }
                )
                |> Task.WhenAll
            return
                NewFile { Hash = latestHash; Rows = encoded }
    }

    let processIntoChunks (exporter, chunker) rows =
        rows
        |> Array.map exporter
        |> Array.chunkBySize 25000
        |> Array.map chunker
        |> Array.indexed

    let writeToFile name data = File.WriteAllLines(name, data, Encoding.UTF8)
    let writeToBlob connectionString name lines =
        let client = BlobContainerClient (connectionString, "properties")
        let blob = client.GetBlobClient name

        let data = lines |> String.concat "\r" |> BinaryData.FromString
        blob.Upload(data, true) |> ignore

    let write writer (exporter, extension) download =
        let chunks = download.Rows |> processIntoChunks exporter
        for chunk, (lines:string list) in chunks do
            writer $"%s{download.Hash}-part-%i{chunk}.%s{extension}" lines

    let createHashRecord writer hash = writer $"hash-%s{hash}.txt" []

    let getAllHashes (connectionString:string) =
        let client = BlobContainerClient (connectionString, "properties")
        client.GetBlobs (prefix = "hash-")
        |> Seq.map (fun b -> b.Name.[5..] |> Path.GetFileNameWithoutExtension)
        |> Set

open type LandRegistry.ComparisonResult

let tryRefreshPrices connectionString requestType = task {
    let toBlob = LandRegistry.writeToBlob connectionString

    let! download =
        let streamTask = LandRegistry.getPropertyStream requestType 
        let existingHashes = LandRegistry.getAllHashes connectionString
        let toLongLat = (LandRegistry.toLongLat connectionString).Value
        LandRegistry.tryGetLatestFile toLongLat streamTask.Result existingHashes

    match download with
    | FileAlreadyExists ->
        return NothingToDo
    | NewFile download ->
        LandRegistry.write toBlob LandRegistry.Exporters.Csv download
        LandRegistry.createHashRecord toBlob download.Hash
        return Completed {| download with Rows = download.Rows.Length; Type = requestType |}
}