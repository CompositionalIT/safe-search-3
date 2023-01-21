module SafeSearch.Ingestion

open System.Net.Http
open FSharp.Data
open System.IO

let httpClient = new HttpClient()

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

    /// This is only used if you wish to seed the postcode lookup manually. Normally this is not required.
    let getAllPostcodes() = task {
        let localPostcodesFilePath = Path.Combine (Directory.GetCurrentDirectory(), "ukpostcodes.csv")

        if not (File.Exists localPostcodesFilePath) then
            let destination = Path.Combine (Directory.GetCurrentDirectory(), "ukpostcodes.zip")
            let! sourceBytes = httpClient.GetByteArrayAsync "https://www.freemaptools.com/download/full-postcodes/ukpostcodes.zip"
            do! File.WriteAllBytesAsync(destination, sourceBytes)
            Compression.ZipFile.ExtractToDirectory(destination, ".")
            File.Delete destination

        return
            (Postcodes.Load localPostcodesFilePath).Rows
            |> Seq.choose(fun r ->
                match r.Latitude, r.Longitude with
                | Some lat, Some long -> Some {| Postcode = r.Postcode; Geo = {| Lat = float lat; Long = float long |} |}
                | _ -> None)
    }

module Transactions =
    type PricePaid = CsvProvider<"price-paid-schema.csv", PreferOptionals = true, Schema="Date=Date">
    /// Gets the latest monthly update of house price sales from the UK Land Registry.
    let downloadTransactions () = task {
        let destination = Path.Combine(Directory.GetCurrentDirectory(), "pp-monthly-update-new-version.csv")

        let! bytes = httpClient.GetByteArrayAsync("http://prod.publicdata.landregistry.gov.uk.s3-website-eu-west-1.amazonaws.com/pp-monthly-update-new-version.csv")
        do! File.WriteAllBytesAsync(destination, bytes)
        let! data = PricePaid.AsyncLoad destination
        return data.Rows |> Seq.toArray
    }