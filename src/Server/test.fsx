#r "nuget:FSharp.Data"

open FSharp.Data
open System.Net.Http

[<Literal>]
let Path =  __SOURCE_DIRECTORY__ + "/price-paid-schema.csv"

type PricePaid = CsvProvider<Path, PreferOptionals = true, Schema="Date=Date">

let client = new HttpClient()
let stream = client.GetStreamAsync("http://prod.publicdata.landregistry.gov.uk.s3-website-eu-west-1.amazonaws.com/pp-monthly-update-new-version.csv").Result

let sr = new System.IO.StreamReader (stream)
let lines =
    let output = ResizeArray ()
    while not sr.EndOfStream do
        output.Add (sr.ReadLine())
    output.ToArray()

let encode (prop:PricePaid.Row) =
    let geo = None
    {|
        TransactionId = string prop.TransactionId
        Price = prop.Price
        DateOfTransfer = prop.Date
        PostCode = prop.Postcode |> Option.toObj
        PropertyType = prop.PropertyType |> Option.toObj
        Build = prop.Duration
        Contract = prop.``Old/New``
        Building = [ prop.PAON; yield! Option.toList prop.SAON ] |> String.concat " "
        Street = prop.Street |> Option.toObj
        Locality = prop.Locality |> Option.toObj
        Town = prop.``Town/City``
        District = prop.District
        County = prop.County
        Geo =
            readOnlyDict <|
                match geo with
                | Some (lat:float, long) ->
                    [
                        "type", box "Point"
                        "coordinates", box [| long; lat |]
                    ]
                | None ->
                    []
    |}

let x =
    lines.[0]
    |> PricePaid.ParseRows
    |> Seq.toArray
    |> Seq.head

x
|> encode
|> System.Text.Json.JsonSerializer.Serialize