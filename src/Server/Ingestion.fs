module Ingestion

open FSharp.Control
open FSharp.Control.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open System.Net.Http

type RefreshType = LatestMonth | Year of int
type RefreshResult = NothingToDo | Completed of {| Type : RefreshType; Rows : int; Hash : string |}

module LandRegistry =
    open Azure.Storage.Blobs
    open FSharp.Data
    open System.Security.Cryptography
    open System
    open System.Collections.Generic
    open System.IO
    open System.Threading.Tasks
    open System.Text
    open System.Text.Encodings.Web
    open System.Text.Json

    type PricePaid = CsvProvider<const (__SOURCE_DIRECTORY__ + "/price-paid-schema.csv"), PreferOptionals = true, Schema="Date=Date">
    type PostcodeResult = PostcodeResult of Shared.Geo
    type PricePaidAndGeo = { Property : PricePaid.Row; GeoLocation : PostcodeResult option }
    type HashedDownload = { Hash : string; Rows : PricePaidAndGeo array }
    type ComparisonResult = DataAlreadyExists | NewDataAvailable of HashedDownload

    let md5 = MD5.Create ()

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
    let asHash (data:byte array) =
        data
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
                | Some (PostcodeResult geo) ->
                    "Geo",
                    [
                        "type", box "Point"
                        "coordinates", box [| geo.Long; geo.Lat |]
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
                | Some (PostcodeResult geo) ->
                    [
                        "type", box "Point"
                        "coordinates", box [| geo.Long; geo.Lat |]
                    ]
                    |> readOnlyDict
                    |> toJson
                | None ->
                    ""
            |]
            |> Array.map (fun s ->
                let escaped = s.Replace("\"", "\"\"")
                $"\"{escaped}\"")
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

    let httpClient = new HttpClient ()
    let getPropertyData requestType cancellationToken =
        let uri =
            match requestType with
            | LatestMonth -> Uris.latestMonth
            | Year year -> Uris.forYear year
        httpClient.GetByteArrayAsync (uri, cancellationToken)

    let private noOp = Task.FromResult None
    let enrichPropertiesWithGeoLocation tryGetGeo (propertyData:byte array) = task {
        use stream = new MemoryStream (propertyData)
        stream.Position <- 0L
        let rows = PricePaid.Load stream
        let tasks = [
            for line in rows.Rows do
                task {
                    let! postcode =
                        match line.Postcode with
                        | None -> noOp
                        | Some postcode -> tryGetGeo postcode
                    return { Property = line; GeoLocation = postcode |> Option.map PostcodeResult }
                }
        ]
        return! Task.WhenAll tasks
    }

    let processIntoChunks (exporter, chunker) rows =
        rows
        |> Array.map exporter
        |> Array.chunkBySize 25000
        |> Array.map chunker
        |> Array.indexed

    let writeToFile name data = File.WriteAllLines(name, data, Encoding.UTF8)
    let writeToBlob connectionString cancellationToken name lines =
        let client = BlobContainerClient (connectionString, "properties")
        let blob = client.GetBlobClient name

        let data = lines |> String.concat "\r" |> BinaryData.FromString
        blob.UploadAsync (data, true, cancellationToken) :> Task

    let writeAllProperties writer (exporter, extension) download = task {
        let chunks = download.Rows |> processIntoChunks exporter
        let tasks = [
            for chunk, (lines:string list) in chunks do
                yield writer $"%s{download.Hash}-part-%i{chunk}.%s{extension}" lines
        ]
        do! Task.WhenAll tasks
    }

    let createHashRecord writer hash = writer $"hash-%s{hash}.txt" []

    let getAllHashes (connectionString:string) cancellationToken = task {
        let client = BlobContainerClient (connectionString, "properties")
        let! blobs =
            client.GetBlobsAsync (prefix = "hash-", cancellationToken = cancellationToken)
            |> AsyncSeq.ofAsyncEnum
            |> AsyncSeq.toArrayAsync

        return
            blobs
            |> Array.map (fun blob -> blob.Name.[5..] |> Path.GetFileNameWithoutExtension)
            |> Set
    }

open type LandRegistry.ComparisonResult
open System.Threading.Tasks
open System
open Microsoft.Extensions.Configuration
open System.Diagnostics
open Azure.Search.Documents.Indexes
open Azure
open Helpers
open Search
open Azure.Data.Tables
open Fake.Core

let tryRefreshPrices connectionString cancellationToken (logger:ILogger) refreshType = task {
    let blobWriter = LandRegistry.writeToBlob connectionString cancellationToken

    let! download = task {
        logger.LogInformation "Downloading latest price paid data..."
        let! propertyData = LandRegistry.getPropertyData refreshType cancellationToken

        let! existingHashes = LandRegistry.getAllHashes connectionString cancellationToken
        let latestHash = propertyData |> LandRegistry.asHash
        logger.LogInformation("Comparing latest hash '{LatestHash}' against {Count} existing hashes.", latestHash, existingHashes.Count)
        if existingHashes.Contains latestHash then
            return LandRegistry.DataAlreadyExists
        else
            let tryGetGeo = (GeoLookup.createTryGetGeoCached connectionString cancellationToken).Value
            logger.LogInformation "Enriching properties with geo location information"
            let! encoded = LandRegistry.enrichPropertiesWithGeoLocation tryGetGeo propertyData
            return NewDataAvailable { Hash = latestHash; Rows = encoded }
    }

    match download with
    | DataAlreadyExists ->
        logger.LogInformation "The data already exists."
        return NothingToDo
    | NewDataAvailable download ->
        logger.LogInformation("Downloaded and enriched {Transactions} transactions. Now saving to storage.", download.Rows.Length)
        do! LandRegistry.writeAllProperties blobWriter LandRegistry.Exporters.Csv download
        do! LandRegistry.createHashRecord blobWriter download.Hash
        return Completed {| download with Rows = download.Rows.Length; Type = refreshType |}
}

let DELAY_BETWEEN_CHECKS = TimeSpan.FromDays 7.

/// Regularly checks for new price data
type PricePaidDownloader (logger:ILogger<PricePaidDownloader>, config:IConfiguration) =
    inherit BackgroundService ()
    override this.ExecuteAsync cancellationToken =
        let backgroundWork =
            task {
                let connectionString = config.["storageConnectionString"]
                let searchEndpoint = Uri $"https://{config.SearchIndexName}.search.windows.net"
                let searchCredential = AzureKeyCredential config.SearchIndexKey
                let siCldient = SearchIndexClient(searchEndpoint, searchCredential)
                if siCldient.GetIndexes() |> Seq.isEmpty then
                    Management.createIndex(searchEndpoint, searchCredential)
                    Management.createBlobDataSource connectionString (searchEndpoint, searchCredential)
                    Management.createCsvIndexer (searchEndpoint, searchCredential)

                logger.LogInformation "Price Paid Data background download worker has started."

                // Put an initial delay for the first check
                do! Task.Delay (TimeSpan.FromSeconds 30., cancellationToken)

                while not cancellationToken.IsCancellationRequested do
                    logger.LogInformation "Trying to refresh latest property prices..."
                    let timer = Stopwatch.StartNew ()
                    let! result = tryRefreshPrices connectionString cancellationToken logger LatestMonth
                    timer.Stop ()
                    match result with
                    | NothingToDo ->
                        logger.LogInformation "Check was successful - nothing to do."
                    | Completed stats ->
                        logger.LogInformation ("Successfully ingested {Rows} (hash: {Hash})!", stats.Rows, stats.Hash)
                    logger.LogInformation ("Check took {Seconds} seconds. Now sleeping until next check due in {TimeToNextCheck} hours ({NextCheckDate}).",
                        timer.Elapsed.TotalSeconds,
                        DELAY_BETWEEN_CHECKS.TotalHours,
                        DateTime.UtcNow.Add DELAY_BETWEEN_CHECKS)
                    do! Task.Delay (DELAY_BETWEEN_CHECKS, cancellationToken)
            }
        backgroundWork
            .ContinueWith (
                (fun (_:Task) -> logger.LogInformation "Price Paid Data background download worker has gracefully shut down."),
                TaskContinuationOptions.OnlyOnCanceled)