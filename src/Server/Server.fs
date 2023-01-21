module Server

open Crime
open Fable.Remoting.Giraffe
open Fable.Remoting.Server
open Microsoft.ApplicationInsights.Extensibility
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Saturn
open Serilog
open Shared
open System
open System.Threading
open Helpers

type Foo = { Name: string }

let searchApi (context: HttpContext) =
    let config = context.GetService<IConfiguration>()
    let logger = context.GetService<ILogger<ISearchApi>>()

    let tryGetGeoCache =
        (GeoLookup.createTryGetGeoCached config.StorageConnectionString CancellationToken.None)
            .Value

    {
        FreeText =
            fun request -> async {
                let formattedQuery = Search.FormattedQuery.Build request.Text

                logger.LogInformation(
                    "Free Text search for {QueryText} on index '{SearchIndex}'",
                    formattedQuery.Value,
                    config.SearchName
                )

                let! results =
                    Search.freeTextSearch formattedQuery request.Filters config.SearchName config.SearchKey
                    |> Async.AwaitTask

                logger.LogInformation(
                    "Returned {Results} results for '{QueryText}'.",
                    results.Results.Length,
                    formattedQuery.Value
                )

                return results
            }
        ByLocation =
            fun request -> async {
                logger.LogInformation(
                    "Location search for '{Postcode}' on index '{SearchIndexName}'. Looking up geo-location data.",
                    request.Postcode,
                    config.SearchName
                )

                let! geoLookupResult = tryGetGeoCache request.Postcode |> Async.AwaitTask

                match geoLookupResult with
                | Some geo ->
                    logger.LogInformation("Successfully mapped '{Postcode}' to {Geo}.", request.Postcode, geo)

                    let! results =
                        Search.locationSearch (geo.Long, geo.Lat) request.Filters config.SearchName config.SearchKey
                        |> Async.AwaitTask

                    logger.LogInformation(
                        "Returned {Results} results for '{Postcode}'.",
                        results.Results.Length,
                        request.Postcode
                    )

                    return Ok results
                | None ->
                    logger.LogWarning("No geo-location information found for '{Postcode}'.", request.Postcode)
                    return Error $"No geo-location data exists for the postcode '{request.Postcode}'."
            }
        GetCrimes =
            fun geo -> async {
                logger.LogInformation("Crime search for '{Geo}'...", geo)
                let! crimes = getCrimesNearPosition geo
                logger.LogInformation("Retrieved {Crimes} different crimes.", crimes.Length)
                return crimes
            }
        GetSuggestions =
            fun searchedTerm -> async {
                logger.LogInformation("Looking up suggestions for {SearchTerm}...", searchedTerm)

                let! suggestions =
                    Search.suggestionsSearch searchedTerm config.SearchName config.SearchKey
                    |> Async.AwaitTask

                logger.LogInformation("Identified {Suggestions} suggestions.", suggestions.Length)

                return {
                    Suggestions = suggestions |> Array.map (fun suggestion -> suggestion.ToLower())
                }
            }
    }

let webApp =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.withErrorHandler (fun ex ctx ->
        let ctx: HttpContext = ctx.httpContext
        let logger = ctx.GetService<ILogger<RouteInfo<HttpContext>>>()
        logger.LogError(ex, "Unhandled Exception occurred")
        Ignore)
    |> Remoting.fromContext searchApi
    |> Remoting.buildHttpHandler

type Object with

    member _.Ignore() = ()

let app = application {
    webhost_config (fun hostBuilder ->
        hostBuilder.ConfigureAppConfiguration(fun builder -> builder.AddUserSecrets<Foo>() |> ignore))

    logging (fun loggingBuilder -> loggingBuilder.AddSerilog().Ignore())

    host_config (fun config ->
        config.UseSerilog(fun _ serviceProvider loggerConfig ->
            loggerConfig.WriteTo
                .Console()
                .WriteTo.ApplicationInsights(
                    serviceProvider.GetRequiredService<TelemetryConfiguration>(),
                    TelemetryConverter.Traces
                )
                .WriteTo.File(
                    Formatting.Json.JsonFormatter(closingDelimiter = ",", renderMessage = true),
                    "log.json",
                    rollingInterval = RollingInterval.Hour
                )
                .Ignore()))

    service_config (fun services ->
        services
            .AddHostedService<Ingestion.PricePaidDownloader>()
            .AddApplicationInsightsTelemetry())

    memory_cache
    use_static "public"
    use_gzip
    use_router webApp
}

run app