module Search

open Azure
open Azure.Core.Serialization
open Azure.Search.Documents
open Azure.Search.Documents.Indexes
open Azure.Search.Documents.Models
open Kibalta
open Microsoft.Spatial
open Shared
open System
open System.Text.Json.Serialization
open System.Collections.Generic

[<CLIMutable>]
type SearchableProperty =
    {
        [<SimpleField(IsKey = true)>] TransactionId : string
        [<SimpleField(IsSortable = true, IsFacetable = true, IsFilterable = true)>] Price : int Nullable
        [<SimpleField(IsSortable = true)>] DateOfTransfer : DateTime Nullable
        [<SearchableField(IsSortable = true)>] PostCode : string
        [<SimpleField(IsFacetable = true, IsFilterable = true)>] PropertyType : string
        [<SimpleField(IsFacetable = true, IsFilterable = true)>] Build : string
        [<SimpleField(IsFacetable = true, IsFilterable = true)>] Contract : string
        [<SimpleField (IsSortable = true)>] Building : string
        [<SearchableField(IsSortable = true)>] Street : string
        [<SearchableField(IsFacetable = true, IsFilterable = true)>] Locality : string
        [<SearchableField(IsSortable = true, IsFacetable = true)>] Town : string
        [<SearchableField(IsFacetable = true, IsFilterable = true)>] District : string
        [<SearchableField(IsFacetable = true, IsFilterable = true)>] County : string
        [<SimpleField (IsSortable = true); JsonConverter(typeof<MicrosoftSpatialGeoJsonConverter>)>] Geo : GeographyPoint
    }

module AzureInterop =
    open Filters

    let private buildClient indexName serviceName key =
        let indexClient = SearchIndexClient(Uri $"https://%s{serviceName}.search.windows.net", AzureKeyCredential key)
        indexClient.GetSearchClient indexName

    let private getResults keyword options (searchClient:SearchClient) =
        let response = searchClient.Search<'T> (keyword, options)
        response.Value.GetResults() |> Seq.map(fun r -> r.Document) |> Seq.toList,
        response.Value.Facets

    let private buildFilterExpression appliedFilters =
        match appliedFilters with
            | [] ->  ConstantFilter true
            | filters ->
                filters
                |> List.map (fun f ->
                    let (field, value) = f
                    (field, box value)
                    |> whereEq)
                |> List.reduce (+)

    let search<'T> indexName keyword (filters: (string * string) list) serviceName key =
        let filterParam =
            filters
            |> buildFilterExpression
            |> eval
        let options =
            SearchOptions (Size = 20, Filter = filterParam)
        Facets.All |> List.iter options.Facets.Add
        buildClient indexName serviceName key
        |> getResults keyword options

    let searchByLocation<'T> indexName (long, lat) (filters: (string * string) list) serviceName key =
        let filterParam = buildFilterExpression filters
        let options =
            SearchOptions (
                Size = 20,
                Filter = ((whereGeoDistance "Geo" (long, lat) Lt 20.) |> (+) filterParam |> eval)
            )
        Facets.All |> List.iter options.Facets.Add
        options.OrderBy.Add((ByDistance ("Geo", long, lat, Ascending) ).StringValue)
        buildClient indexName serviceName key
        |> getResults null options

let private toPropertyResult result  =
    {
        BuildDetails =
            {
                PropertyType = result.PropertyType |> PropertyType.Parse
                Build = result.Build |> BuildType.Parse
                Contract = result.Contract |> ContractType.Parse
            }
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
            |> Option.defaultValue DateTime.MinValue
    }

let getFacets (facetResults: IDictionary<string, IList<FacetResult>>) facetName =
    match facetResults.TryGetValue facetName with
    | true, facets ->
        facets
        |> Seq.toList
        |> List.map (fun facet ->
                string facet.Value
            )
        |> Some
    | false, x -> None
    |> Option.defaultValue []
let private toFacetResult (facets: IDictionary<string, IList<FacetResult>>) =
    let getFacets = getFacets facets
    {
        Towns = getFacets "Town"
        Localities = getFacets "Locality"
        Districts = getFacets "District"
        Counties = getFacets "County"
        Prices = getFacets "Price"
    }

let private toSearchResponse (searchableProperties, facets) =
    {
        Results = searchableProperties |> List.map toPropertyResult
        Facets = facets |> toFacetResult
    }
let freeTextSearch keyword filters index key =
    AzureInterop.search "properties" keyword filters index key
    |> toSearchResponse

let locationSearch (long, lat) filters index key =
    AzureInterop.searchByLocation "properties" (long, lat) filters index key
    |> toSearchResponse