module Search

open Azure
open Azure.Core.Serialization
open Azure.Search.Documents
open Azure.Search.Documents.Indexes
open Kibalta
open Microsoft.Spatial
open Shared
open System
open System.Text.Json.Serialization

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
        [<SimpleField (IsSortable = true); JsonConverter(typeof<MicrosoftSpatialGeoJsonConverter>)>] Geo : GeographyPoint
    }

module AzureInterop =
    open Filters

    let private buildClient indexName serviceName key =
        let indexClient = SearchIndexClient(Uri $"https://%s{serviceName}.search.windows.net", AzureKeyCredential key)
        indexClient.GetSearchClient indexName

    let private getResults keyword options (searchClient:SearchClient) =
        let response = searchClient.Search<'T> (keyword, options)
        response.Value.GetResults() |> Seq.map(fun r -> r.Document) |> Seq.toList

    let search<'T> indexName keyword serviceName key =
        buildClient indexName serviceName key
        |> getResults keyword (SearchOptions (Size = 20))

    let searchByLocation<'T> indexName (long, lat) serviceName key =
        let options =
            SearchOptions (
                Size = 20,
                Filter = (whereGeoDistance "Geo" (long, lat) Lt 20. |> eval)
            )
        options.OrderBy.Add((ByDistance ("Geo", long, lat, Ascending) ).StringValue)
        buildClient indexName serviceName key
        |> getResults null options

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

let freeTextSearch keyword index key =
    AzureInterop.search "properties" keyword index key
    |> List.map toPropertyResult

let locationSearch (long, lat) index key =
    AzureInterop.searchByLocation "properties" (long, lat) index key
    |> List.map toPropertyResult