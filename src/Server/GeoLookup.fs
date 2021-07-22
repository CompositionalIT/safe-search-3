module GeoLookup

open FSharp.Control.Tasks
open Azure.Data.Tables
open System

type Postcode =
    {
        Long : float
        Lat : float
    }
    static member TryParse(entity:TableEntity) =
        match Option.ofNullable (entity.GetDouble "Long"), Option.ofNullable (entity.GetDouble "Lat") with
        | Some long, Some lat ->
            Some
                {
                    Long = long
                    Lat = lat
                }
        | _ ->
            None

let tryGetGeo connectionString (postcode:string) = task {
    match postcode.Split ' ' with
    | [| postcodeA; postcodeB |] ->
        let client = TableClient(connectionString, "postcodes")
        let! response = client.GetEntityAsync<TableEntity>(postcodeA.ToUpper(), postcodeB.ToUpper())
        return response.Value |> Postcode.TryParse
    | _ ->
        return None
}