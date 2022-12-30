module Crime

open FSharp.Data
open Shared

type PoliceUkCrime = JsonProvider<"PoliceUkCrime.json">

let getCrimesNearPosition (geo: Geo) = async {
    let! crimes = PoliceUkCrime.AsyncLoad $"https://data.police.uk/api/crimes-street/all-crime?lat={geo.Lat}&lng={geo.Long}"
    return
        crimes
        |> Array.countBy(fun report -> report.Category)
        |> Array.sortByDescending snd
        |> Array.map(fun (crime, incidents) -> { Crime = crime; Incidents = incidents })
}