module Ingestion

open System.Reactive.Disposables
open System.Threading
open FSharp.Control
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open System.Net.Http

type RefreshType =
    | LatestMonth
    | Year of int

type RefreshResult =
    | NothingToDo
    | Completed of
        {|
            Type: RefreshType
            Rows: int
            Hash: string
        |}

module LandRegistry =
    open Azure.Storage.Blobs
    open FSharp.Data
    open System.Security.Cryptography
    open System
    open System.IO
    open System.Threading.Tasks
    open System.Text
    open System.Text.Encodings.Web
    open System.Text.Json

    type PricePaid =
        CsvProvider<const(__SOURCE_DIRECTORY__ + "/price-paid-schema.csv"), PreferOptionals=true, Schema="Date=Date">

    type PostcodeResult = PostcodeResult of Shared.Geo

    type PricePaidAndGeo = {
        Property: PricePaid.Row
        GeoLocation: PostcodeResult option
    }

    type HashedDownload = {
        Hash: string
        Rows: PricePaidAndGeo array
    }

    type ComparisonResult =
        | DataAlreadyExists
        | NewDataAvailable of HashedDownload

    let md5 = MD5.Create()

    module Uris =
        let latestMonth =
            Uri
                "http://prod.publicdata.landregistry.gov.uk.s3-website-eu-west-1.amazonaws.com/pp-monthly-update-new-version.csv"

        let forYear year =
            Uri $"http://prod.publicdata.landregistry.gov.uk.s3-website-eu-west-1.amazonaws.com/pp-{year}.csv"

    let toJson =
        let options =
            JsonSerializerOptions(
                WriteIndented = false,
                IgnoreNullValues = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                PropertyNameCaseInsensitive = true
            )

        fun (value: obj) -> JsonSerializer.Serialize(value, options)

    let asHash (data: byte array) =
        data |> md5.ComputeHash |> BitConverter.ToString |> String.filter ((<>) '-')

    module Exporters =
        // Export a row for JSON
        let asJsonOutput (row: PricePaidAndGeo) =
            let property, geo = row.Property, row.GeoLocation

            [
                "TransactionId", property.TransactionId |> string |> box
                "Price", property.Price |> box
                "DateOfTransfer", property.Date |> box
                "PostCode", property.Postcode |> Option.toObj |> box
                "PropertyType", property.PropertyType |> Option.toObj |> box
                "Build", property.Duration |> box
                "Contract", property.``Old/New`` |> box
                "Building",
                [ property.PAON; yield! Option.toList property.SAON ]
                |> String.concat " "
                |> box
                "Street", property.Street |> Option.toObj |> box
                "Locality", property.Locality |> Option.toObj |> box
                "Town", property.``Town/City`` |> box
                "District", property.District |> box
                "County", property.County |> box
                match geo with
                | Some(PostcodeResult geo) ->
                    "Geo",
                    [ "type", box "Point"; "coordinates", box [| geo.Long; geo.Lat |] ]
                    |> readOnlyDict
                    |> box
                | None -> ()
            ]
            |> List.filter (snd >> isNull >> not)
            |> readOnlyDict

        // Export a row for CSV
        let asCsvOutput (row: PricePaidAndGeo) =
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
                | Some(PostcodeResult geo) ->
                    [ "type", box "Point"; "coordinates", box [| geo.Long; geo.Lat |] ]
                    |> readOnlyDict
                    |> toJson
                | None -> ""
            |]
            |> Array.map (fun s ->
                let escaped = s.Replace("\"", "\"\"")
                $"\"{escaped}\"")
            |> String.concat ","

        // Combine a chunk of JSON rows
        let processJsonChunk lines =
            lines |> Array.map toJson |> Array.toList

        // Combine a chunk of CSV rows
        let processCsvChunk lines =
            let header =
                [
                    "TransactionId"
                    "Price"
                    "DateOfTransfer"
                    "PostCode"
                    "PropertyType"
                    "Build"
                    "Contract"
                    "Building"
                    "Street"
                    "Locality"
                    "Town"
                    "District"
                    "County"
                    "Geo"
                ]
                |> String.concat ","

            header :: Array.toList lines

        /// Exports the bundled PricePaid + Location data into a serialized format.
        type IPriceDataSerializer<'T> =
            abstract Serializer: PricePaidAndGeo -> 'T
            abstract ChunkHandler: 'T array -> string list
            abstract FileExtension: string

        let private create serializer chunkHandler extension =
            { new IPriceDataSerializer<_> with
                member _.Serializer row = serializer row
                member _.ChunkHandler chunk = chunkHandler chunk
                member _.FileExtension = extension
            }

        let Csv = create asCsvOutput processCsvChunk "csv"
        let Json = create asJsonOutput processJsonChunk "json"

    open Exporters

    let httpClient = new HttpClient()

    let getPropertyData requestType cancellationToken =
        let uri =
            match requestType with
            | LatestMonth -> Uris.latestMonth
            | Year year -> Uris.forYear year

        httpClient.GetByteArrayAsync(uri, cancellationToken)

    let private noOp = Task.FromResult None

    let private reporter (logger: ILogger) transactions postcodes uniquePostcodes =
        MailboxProcessor.Start(fun mailbox ->
            let mutable transactions = transactions

            logger.LogInformation(
                "{transactions} transactions in total - {uniquePostcodes} unique postcodes ({duplicatePostcodes} duplicates) and {propertiesWithoutPostcodes} transactions without postcodes.",
                transactions,
                uniquePostcodes,
                postcodes - uniquePostcodes,
                transactions - postcodes
            )

            async {
                while true do
                    do! mailbox.Receive()
                    transactions <- transactions - 1

                    if transactions % 5000 = 0 then
                        logger.LogInformation("{transactions} remaining", transactions)

            })

    let enrichPropertiesWithGeoLocation
        (logger: ILogger)
        (cancellationToken: CancellationToken)
        tryGetGeo
        (propertyData: byte array)
        =
        task {
            use stream = new MemoryStream(propertyData)
            stream.Position <- 0L
            let transactions = PricePaid.Load stream |> fun r -> r.Rows |> Seq.toArray
            let postcodes = transactions |> Array.choose (fun t -> t.Postcode)
            let uniquePostcodes = postcodes |> Array.distinct |> Array.length

            let signal =
                (reporter logger transactions.Length postcodes.Length uniquePostcodes).Post

            let output = ResizeArray()

            for chunk in transactions |> Array.chunkBySize 500 do
                let tasks = [
                    for transaction in chunk do
                        task {
                            let! geoData =
                                match transaction.Postcode with
                                | None -> noOp
                                | Some postcode -> tryGetGeo postcode

                            signal ()

                            return {
                                Property = transaction
                                GeoLocation = geoData |> Option.map PostcodeResult
                            }
                        }
                ]

                let! results = Task.WhenAll(tasks).WaitAsync(cancellationToken)
                output.Add results

            return output |> Seq.concat |> Seq.toArray
        }

    /// Splits a set of price paid + geo data into chunks of 25000 rows and serializes them.
    let processIntoChunks (priceSerializer: IPriceDataSerializer<_>) rows =
        rows
        |> Array.map priceSerializer.Serializer
        |> Array.chunkBySize 25000
        |> Array.map priceSerializer.ChunkHandler
        |> Array.indexed

    let writeToFile name data =
        File.WriteAllLines(name, data, Encoding.UTF8)

    let writeToBlob connectionString cancellationToken name lines =
        let client = BlobContainerClient(connectionString, "properties")
        let blob = client.GetBlobClient name

        let data = lines |> String.concat "\r" |> BinaryData.FromString
        blob.UploadAsync(data, true, cancellationToken) :> Task

    let writeAllProperties writer (priceSerializer: IPriceDataSerializer<_>) download = task {
        let chunks = download.Rows |> processIntoChunks priceSerializer

        let tasks = [
            for chunk, lines in chunks do
                yield writer $"%s{download.Hash}-part-%i{chunk}.%s{priceSerializer.FileExtension}" lines
        ]

        do! Task.WhenAll tasks
    }

    let createHashRecord writer hash = writer $"hash-%s{hash}.txt" []

    let getAllHashes (connectionString: string) cancellationToken = task {
        let client = BlobContainerClient(connectionString, "properties")

        let! blobs =
            client.GetBlobsAsync(prefix = "hash-", cancellationToken = cancellationToken)
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

let tryRefreshPrices connectionString cancellationToken (logger: ILogger) refreshType = task {
    let blobWriter = LandRegistry.writeToBlob connectionString cancellationToken

    let! download = task {
        logger.LogInformation "Downloading latest price paid data..."
        let! propertyData = LandRegistry.getPropertyData refreshType cancellationToken
        let! existingHashes = LandRegistry.getAllHashes connectionString cancellationToken
        let latestHash = LandRegistry.asHash propertyData

        logger.LogInformation(
            "Comparing latest hash '{LatestHash}' against {Count} existing hashes.",
            latestHash,
            existingHashes.Count
        )

        if existingHashes.Contains latestHash then
            return LandRegistry.DataAlreadyExists
        else
            logger.LogInformation "New dataset - enriching properties with geo location information"

            let tryGetGeo =
                (GeoLookup.createTryGetGeoCached connectionString cancellationToken).Value

            let! encoded = LandRegistry.enrichPropertiesWithGeoLocation logger cancellationToken tryGetGeo propertyData

            return NewDataAvailable { Hash = latestHash; Rows = encoded }
    }

    match download with
    | DataAlreadyExists ->
        logger.LogInformation "The data already exists."
        return NothingToDo
    | NewDataAvailable download ->
        logger.LogInformation(
            "Downloaded and enriched {Transactions} transactions. Now saving to storage.",
            download.Rows.Length
        )

        do! LandRegistry.writeAllProperties blobWriter LandRegistry.Exporters.Csv download
        do! LandRegistry.createHashRecord blobWriter download.Hash

        return
            Completed
                {| download with
                    Rows = download.Rows.Length
                    Type = refreshType
                |}
}

let DELAY_BETWEEN_CHECKS = TimeSpan.FromDays 7.

let primeSearchIndex (logger: ILogger) (config: IConfiguration) =
    logger.LogInformation $"Connecting to search index: '{config.SearchName}'."
    let searchEndpoint = Uri $"https://{config.SearchName}.search.windows.net"
    let searchCredential = AzureKeyCredential config.SearchKey
    let siClient = SearchIndexClient(searchEndpoint, searchCredential)

    logger.LogInformation "Checking if search index exists..."

    if siClient.GetIndexes() |> Seq.isEmpty then
        logger.LogInformation "Index does not exist, creating index, data source and indexer..."
        Management.createIndex (searchEndpoint, searchCredential)
        Management.createBlobDataSource config.StorageConnectionString (searchEndpoint, searchCredential)
        Management.createCsvIndexer (searchEndpoint, searchCredential)
        logger.LogInformation "Created index."
    else
        logger.LogInformation "Index already exists, nothing to do."


/// Regularly checks for new price data
type PricePaidDownloader(logger: ILogger<PricePaidDownloader>, config: IConfiguration) =
    inherit BackgroundService()

    override this.ExecuteAsync cancellationToken =
        let backgroundWork = task {
            logger.LogInformation "Price Paid Data background download worker has started."

            // First check if the search index needs to be primed.
            primeSearchIndex logger config

            // Put an initial delay for the first check
            do! Task.Delay(TimeSpan.FromSeconds 0., cancellationToken)

            while not cancellationToken.IsCancellationRequested do
                logger.LogInformation "Trying to refresh latest property prices..."
                let timer = Stopwatch.StartNew()
                let! result = tryRefreshPrices config.StorageConnectionString cancellationToken logger LatestMonth
                timer.Stop()

                match result with
                | NothingToDo -> logger.LogInformation "Check was successful - nothing to do."
                | Completed stats ->
                    logger.LogInformation("Successfully ingested {Rows} (hash: {Hash})!", stats.Rows, stats.Hash)

                logger.LogInformation(
                    "Check took {Seconds} seconds. Now sleeping until next check due in {TimeToNextCheck} hours ({NextCheckDate}).",
                    timer.Elapsed.TotalSeconds,
                    DELAY_BETWEEN_CHECKS.TotalHours,
                    DateTime.UtcNow.Add DELAY_BETWEEN_CHECKS
                )

                do! Task.Delay(DELAY_BETWEEN_CHECKS, cancellationToken)
        }

        backgroundWork.ContinueWith(
            (fun (_: Task) ->
                logger.LogInformation "Price Paid Data background download worker has gracefully shut down."),
            TaskContinuationOptions.OnlyOnCanceled
        )