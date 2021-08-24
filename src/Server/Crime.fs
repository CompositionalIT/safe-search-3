module Crime

open FSharp.Data
open Shared

type PoliceUkCrime = JsonProvider<"https://data.police.uk/api/crimes-street/all-crime?lat=51.5074&lng=0.1278">

let getCrimesNearPosition (geo: Geo) =
    (geo.Lat, geo.Long)
    ||> sprintf "https://data.police.uk/api/crimes-street/all-crime?lat=%f&lng=%f"
    |> PoliceUkCrime.AsyncLoad