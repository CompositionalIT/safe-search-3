module Server

open Crime
open Fable.Remoting.Giraffe
open Fable.Remoting.Server
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Saturn
open Shared
open System.Threading

type Foo = { Name : string }

type IConfiguration with
    member this.SearchIndexName = this.["searchName"]
    member this.SearchIndexKey = this.["searchKey"]
    member this.StorageConnectionString = this.["storageConnectionString"]

let searchApi (context:HttpContext) =
    let config = context.GetService<IConfiguration>()
    let logger = context.GetService<ILogger<ISearchApi>>()
    let tryGetGeoCache = (GeoLookup.createTryGetGeoCached config.StorageConnectionString CancellationToken.None).Value

    {
        FreeText = fun request -> async {
            let formattedQuery = $"\"{request.Text}\""
            logger.LogInformation $"Free Text search for {formattedQuery} on index '{config.SearchIndexName}'"
            let results = Search.freeTextSearch formattedQuery request.Filters config.SearchIndexName config.SearchIndexKey
            logger.LogInformation $"Returned {results.Results.Length} results for '{formattedQuery}'."
            return results
        }
        ByLocation = fun request -> async {
            logger.LogInformation $"Location search for '{request.Postcode}' on index '{config.SearchIndexName}'. Looking up geo-location data."
            let! geoLookupResult = tryGetGeoCache request.Postcode |> Async.AwaitTask
            return
                match geoLookupResult with
                | Some geo ->
                    logger.LogInformation $"Successfully mapped '{request.Postcode}' to {(geo.Long, geo.Lat)}."
                    let results = Search.locationSearch (geo.Long, geo.Lat) request.Filters config.SearchIndexName config.SearchIndexKey
                    logger.LogInformation $"Returned {results.Results.Length} results for '{request.Postcode}'."
                    Ok results
                | None ->
                    logger.LogWarning $"No geo-location information found for '{request.Postcode}'."
                    Error $"No geo-location data exists for the postcode '{request.Postcode}'."
        }
        GetCrimes = fun geo -> async {
            let! reports = getCrimesNearPosition geo
            let crimes =
                reports
                |> Array.countBy(fun report -> report.Category)
                |> Array.sortByDescending snd
                |> Array.map(fun (crime, incidents) -> { Crime = crime; Incidents = incidents })
            return crimes
        }
        GetSuggestions = fun searchedTerm -> async {
            let results =
                Search.suggestionsSearch searchedTerm config.SearchIndexName config.SearchIndexKey
                |> Seq.map (fun suggestion -> suggestion.ToLower())
                |> Seq.toArray
            return { Suggestions = results }
        }
    }

let webApp =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.withErrorHandler (fun ex _ -> printfn "%O" ex; Ignore)
    |> Remoting.fromContext searchApi
    |> Remoting.buildHttpHandler

let app =
    application {
        logging (fun logging -> logging.AddConsole() |> ignore)
        webhost_config (fun config -> config.ConfigureAppConfiguration(fun builder -> builder.AddUserSecrets<Foo>() |> ignore))
        service_config (fun config -> config.AddHostedService<Ingestion.PricePaidDownloader>())
        memory_cache
        use_static "public"
        use_gzip
        use_router webApp
    }

run app