module GeoLookup

open Azure.Data.Tables
open System.Threading.Tasks
open Microsoft.Extensions.Caching.Memory
open Shared

let (|ValidLatLong|_|) v =
    if v < -90. then None
    elif v > 90. then None
    elif v = 0. then None
    else Some (ValidLatLong v)

let rec retry retries (thunk:unit -> Task<_>) = task {
    try
        return! thunk ()
    with
    | ex ->
        if retries > 0 then
            return! retry (retries - 1) thunk
        else
            raise ex
            return Unchecked.defaultof<'T>
}

type TableEntity with
    member this.ToLongLat =
        match Option.ofNullable (this.GetDouble "Long"), Option.ofNullable (this.GetDouble "Lat") with
        | Some (ValidLatLong long), Some (ValidLatLong lat) ->
            Some { Long = long; Lat = lat }
        | _ ->
            None

let tryGetGeo connectionString cancellationToken (postcode:string) = task {
    match postcode.Split ' ' with
    | [| postcodeA; postcodeB |] ->
        let client = TableClient (connectionString, "postcodes")
        try
            let! response = retry 3 (fun () -> client.GetEntityAsync<TableEntity> (postcodeA.ToUpper(), postcodeB.ToUpper(), cancellationToken = cancellationToken))
            return response.Value.ToLongLat
        with
        | _ ->
            return None

    | _ ->
        return None
}

let createTryGetGeoCached connectionString cancellationToken =
    let geoCache = new MemoryCache (MemoryCacheOptions())
    lazy (fun postcode ->
        geoCache.GetOrCreateAsync(postcode, fun (e:ICacheEntry) -> tryGetGeo connectionString cancellationToken postcode)
    )

