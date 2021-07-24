module Server

open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.Hosting
open Saturn

open Shared

type Foo = { Name : string }

let searchApi (context:HttpContext) =
    let config = context.GetService<IConfiguration>()
    let logger = context.GetService<ILogger<ISearchApi>>()
    {
        FreeText = fun request -> async {
            logger.LogInformation $"""Searching for '{request.Text}' on index '{config.["search-name"]}'"""
            let results = Search.freeTextSearch request.Text config.["search-name"] config.["search-key"]
            return results
        }
        ByLocation = fun request -> async {
            logger.LogInformation $"""Searching for '{request.Postcode}' on index '{config.["search-name"]}'"""
            let! geoLookupResult = GeoLookup.tryGetGeo config.["storage-connection-string"] request.Postcode |> Async.AwaitTask
            return
                match geoLookupResult with
                | Some geo ->
                    logger.LogInformation $"{request.Postcode} => {(geo.Long, geo.Lat)}."
                    let results = Search.locationSearch (geo.Long, geo.Lat) config.["search-name"] config.["search-key"]
                    Ok results
                | None ->
                    Error "Invalid postcode"
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
        url "http://0.0.0.0:8085"
        logging (fun logging -> logging.AddConsole() |> ignore)
        webhost_config (fun config ->
            config.ConfigureAppConfiguration(fun c -> c.AddUserSecrets<Foo>() |> ignore)
        )
        memory_cache
        use_static "public"
        use_gzip
        use_router webApp
    }

run app
