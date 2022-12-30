module SafeSearch.Ingestion

open FSharp.Control.Tasks
open FSharp.Data
open System
open System.IO
open System.Net

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
            | Some lat, Some long -> Some {| Postcode = r.Postcode; Geo = {| Lat = float lat; Long = float long |} |}
            | _ -> None)

module Transactions =
    type PricePaid = CsvProvider<"price-paid-schema.csv", PreferOptionals = true, Schema="Date=Date">
    /// Gets the latest monthly update of house price sales from the UK Land Registry.
    let downloadTransactions () = task {
        let path = Path.Combine(Directory.GetCurrentDirectory(), "pp-monthly-update-new-version.csv")

        let wc = new WebClient()
        do! wc.DownloadFileTaskAsync(Uri "http://prod.publicdata.landregistry.gov.uk.s3-website-eu-west-1.amazonaws.com/pp-monthly-update-new-version.csv", path)

        let data = PricePaid.Load path
        return data.Rows |> Seq.toArray
    }