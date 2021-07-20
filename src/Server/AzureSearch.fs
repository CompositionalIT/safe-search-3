module Search

open Azure
open Azure.Search.Documents
open Azure.Search.Documents.Indexes
open Shared
open System
open Microsoft.Spatial
open System.Text.Json.Serialization
open Azure.Core.Serialization

[<CLIMutable>]
type SearchableProperty =
    {
        [<SimpleField(IsKey = true)>] TransactionId : string
        [<SimpleField(IsSortable = true)>] Price : int Nullable
        [<SimpleField(IsSortable = true)>] DateOfTransfer : DateTime Nullable
        [<SearchableField(IsSortable = true)>] PostCode : string
        [<SimpleField>] PropertyType : string
        [<SimpleField>] Build : string
        [<SimpleField>] Contract : string
        [<SimpleField (IsSortable = true)>] Building : string
        [<SearchableField(IsSortable = true)>] Street : string
        [<SearchableField>] Locality : string
        [<SearchableField(IsSortable = true)>] Town : string
        [<SearchableField>] District : string
        [<SearchableField>] County : string
        [<SimpleField (IsSortable = true)>]
        [<JsonConverter(typeof<MicrosoftSpatialGeoJsonConverter>)>] Geo : GeographyPoint }

let search<'T> indexName keyword serviceName key =
    let indexClient = SearchIndexClient(Uri $"https://%s{serviceName}.search.windows.net", AzureKeyCredential key)
    let searchClient = indexClient.GetSearchClient indexName
    let response = searchClient.Search<'T> (Option.toObj keyword, SearchOptions(Size = 20))
    response.Value.GetResults() |> Seq.map(fun r -> r.Document) |> Seq.toList

let private toPropertyResult result  =
    {
        BuildDetails =
            {
                PropertyType = result.PropertyType |> PropertyType.Parse
                Build = result.Build |> BuildType.Parse
                Contract = result.Contract |> ContractType.Parse }
        Address =
            {
                Building = result.Building
                Street = result.Street |> Option.ofObj
                Locality = result.Locality |> Option.ofObj
                TownCity = result.Town
                District = result.District
                County = result.County
                PostCode = result.PostCode |> Option.ofObj
                GeoLocation =
                    result.Geo
                    |> Option.ofObj
                    |> Option.map (fun geo -> { Lat = geo.Latitude; Long = geo.Longitude })
            }
        Price =
            result.Price
            |> Option.ofNullable
            |> Option.defaultValue 0
        DateOfTransfer =
            result.DateOfTransfer
            |> Option.ofNullable
            |> Option.defaultValue DateTime.MinValue }

let searchProperties keyword index key =
    search<SearchableProperty> "properties" keyword index key
    |> List.map toPropertyResult