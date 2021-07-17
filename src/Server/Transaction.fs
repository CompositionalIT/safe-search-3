module SafeSearch.Ingestion

open FSharp.Control.Tasks
open FSharp.Data
open System
open System.IO
open System.Net
open Fable.Remoting.Json

// open Giraffe
// open SafeSearch
// open SafeSearch.Search
// open Saturn
// open Thoth.Json.Net

module Postcodes =
    type Postcodes = CsvProvider<"uk-postcodes-schema.csv", PreferOptionals = true, Schema="Latitude=decimal option,Longitude=decimal option">
    let tryGeoPostcode (row:Postcodes.Row) =
        match row.Postcode.Split ' ', row.Latitude, row.Longitude with
        | [| partA; partB |], Some latitude, Some longitude ->
            Some {| PostCode = partA, partB
                    Latitude = float latitude
                    Longitude = float longitude |}
        | _ ->
            None

    let getAllPostcodes() =
        let localPostcodesFilePath = Path.Combine (Directory.GetCurrentDirectory(), "ukpostcodes.csv")

        if not (File.Exists localPostcodesFilePath) then
            let zipPath = Path.Combine (Directory.GetCurrentDirectory(), "ukpostcodes.zip")
            use wc = new WebClient()
            wc.DownloadFile(Uri "https://www.freemaptools.com/download/full-postcodes/ukpostcodes.zip", zipPath)
            Compression.ZipFile.ExtractToDirectory(zipPath, ".")
            File.Delete zipPath

        (Postcodes.Load localPostcodesFilePath).Rows
        |> Seq.choose(fun r ->
            match r.Latitude, r.Longitude with
            | Some lat, Some long -> Some (r.Postcode, (float lat, float long))
            | _ -> None)

module Transactions =
    type PricePaid = CsvProvider<"price-paid-schema.csv", PreferOptionals = true, Schema="Date=Date">
    let downloadTransactions () = task {
        let path = Path.Combine(Directory.GetCurrentDirectory(), "pp-monthly-update-new-version.csv")

        let wc = new WebClient()
        do! wc.DownloadFileTaskAsync(Uri "http://prod.publicdata.landregistry.gov.uk.s3-website-eu-west-1.amazonaws.com/pp-monthly-update-new-version.csv", path)

        let data = PricePaid.Load path
        return data.Rows |> Seq.toArray
    }
        // let doProgressImport onComplete (txnData:PricePaid.Row seq) = task {
        //     let geoLookup =
        //         let requiredPostcodes = txnData |> Seq.choose(fun r -> r.Postcode) |> Set
        //         Postcodes.getAllPostcodes() |> Seq.filter(fst >> requiredPostcodes.Contains) |> Map

        //     let encode (prop:PricePaid.Row) =
        //         let geo = prop.Postcode |> Option.bind geoLookup.TryFind
        //         Encode.object [
        //             "TransactionId", Encode.string (prop.TransactionId.ToString())
        //             "Price", Encode.int prop.Price
        //             "DateOfTransfer", Encode.datetime prop.Date
        //             "PostCode", Encode.string (prop.Postcode |> Option.toObj)
        //             "PropertyType", Encode.string (prop.PropertyType |> Option.toObj)
        //             "Build", Encode.string prop.Duration
        //             "Contract", Encode.string prop.``Old/New``
        //             "Building", [ Some prop.PAON; prop.SAON ] |> List.choose id |> String.concat " " |> Encode.string
        //             "Street", Encode.string (prop.Street |> Option.toObj)
        //             "Locality", Encode.string (prop.Locality |> Option.toObj)
        //             "Town", Encode.string prop.``Town/City``
        //             "District", Encode.string prop.District
        //             "County", Encode.string prop.County

        //             match geo with
        //             | Some (lat, long) ->
        //                 "Geo", Encode.object [
        //                     "type", Encode.string "Point"
        //                     "coordinates", Encode.array [| Encode.float long; Encode.float lat |]
        //                 ]
        //             | None ->
        //                 ()
        //         ]

        //     for (i, chunk) in (txnData |> Seq.chunkBySize 10000 |> Seq.indexed) do
        //         let json = chunk |> Array.map encode |> Encode.array |> Encode.toString 4
        //         printfn "Uploading %d..." i
        //         let b = Storage.Azure.Containers.properties.[sprintf "%d.json" i]
        //         do! b.AsCloudBlockBlob(storageConnection).UploadTextAsync(json)
        //         onComplete(chunk.Length, 0)
        //     }

        // rowCount, txnData, doProgressImport

    // let propertyResultIngester = Ingestion.buildIngester<PricePaid.Row>()

    // let ingest (searcher:ISearch) storageConnection next ctx = task {
    //     searcher.Clear()
    //     let rowsToImport, txnData, importer = uploadTransactions storageConnection
    //     propertyResultIngester.IngestData(rowsToImport, txnData, importer)
    //     return! json rowsToImport next ctx }

    // let getStats (searcher:ISearch) next ctx = task {
    //     let! documents = searcher.Documents()
    //     let! storeStatus = propertyResultIngester.GetStoreStatus()
    //     let indexStats =
    //         { DocumentCount = documents
    //           Status = storeStatus.AsIndexState }

    //     return! json indexStats next ctx }

    // let createRouter searcher storageConnection = router {
    //     get "import" (ingest searcher storageConnection)
    //     get "stats" (getStats searcher) }