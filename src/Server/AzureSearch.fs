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
        [<SimpleField (IsSortable = true, IsFilterable = true); JsonConverter(typeof<MicrosoftSpatialGeoJsonConverter>)>] Geo : GeographyPoint
    }

let PROPERTIES_INDEX = "properties"
let SUGGESTER_NAME = "suggester"

module Fields =
    let DATE_OF_TRANSFER = "DateOfTransfer"
    let GEO = "Geo"
    let TOWN = "Town"
    let STREET = "Street"
    let LOCALITY = "Locality"
    let DISTRICT = "District"
    let COUNTY = "County"
    let PRICE = "Price"

module AzureInterop =
    open Filters

    let private buildClient indexName serviceName key =
        let indexClient = SearchIndexClient(Uri $"https://%s{serviceName}.search.windows.net", AzureKeyCredential key)
        indexClient.GetSearchClient indexName

    let private getResults keyword (options:SearchOptions) (searchClient:SearchClient) =
        let response = searchClient.Search<'T> (keyword, options)
        let results = response.Value.GetResults() |> Seq.map(fun r -> r.Document) |> Seq.toList

        results, response.Value.Facets

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
        let options = SearchOptions (Size = 20, Filter = filterParam)
        options.OrderBy.Add(ByField(Fields.DATE_OF_TRANSFER, Descending).StringValue)
        for facet in Facets.All do options.Facets.Add facet

        buildClient indexName serviceName key
        |> getResults keyword options

    let searchByLocation<'T> indexName (long, lat) (filters: (string * string) list) serviceName key =
        let filterParam = buildFilterExpression filters
        let options =
            SearchOptions (
                Size = 20,
                Filter = ((whereGeoDistance Fields.GEO (long, lat) Lt 20.) |> (+) filterParam |> eval)
            )
        for facet in Facets.All do options.Facets.Add facet
        options.OrderBy.Add(ByDistance(Fields.GEO, long, lat, Ascending).StringValue)
        options.OrderBy.Add(ByField(Fields.DATE_OF_TRANSFER, Descending).StringValue)

        buildClient indexName serviceName key
        |> getResults null options

    let searchSuggestions<'T> indexName searchedTerm serviceName key =
        let searchClient = buildClient indexName serviceName key
        let response = searchClient.Suggest(searchedTerm, SUGGESTER_NAME)

        response.Value.Results
        |> Seq.map (fun r -> r.Text)
        |> Seq.distinct

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
        Towns = getFacets Fields.TOWN
        Localities = getFacets Fields.LOCALITY
        Districts = getFacets Fields.DISTRICT
        Counties = getFacets Fields.COUNTY
        Prices = getFacets Fields.PRICE
    }

let private toSearchResponse (searchableProperties, facets) =
    {
        Results = searchableProperties |> List.map toPropertyResult
        Facets = facets |> toFacetResult
    }
let freeTextSearch keyword filters index key =
    AzureInterop.search PROPERTIES_INDEX keyword filters index key
    |> toSearchResponse

let locationSearch (long, lat) filters index key =
    AzureInterop.searchByLocation PROPERTIES_INDEX (long, lat) filters index key
    |> toSearchResponse

let suggestionsSearch searchedTerm index key =
    AzureInterop.searchSuggestions PROPERTIES_INDEX searchedTerm index key

module Management =
    open Azure.Search.Documents.Indexes.Models

    // Build and configure the search index store itself
    let createIndex indexConnection =
        let searchIndex =
            let fieldBuilder = FieldBuilder ()
            let searchFields = fieldBuilder.Build typeof<SearchableProperty>
            SearchIndex (PROPERTIES_INDEX, searchFields)
        searchIndex.Suggesters.Add (SearchSuggester (SUGGESTER_NAME, Fields.STREET, Fields.LOCALITY, Fields.TOWN, Fields.DISTRICT, Fields.COUNTY))

        let indexClient = SearchIndexClient (fst indexConnection, snd indexConnection)
        indexClient.DeleteIndex searchIndex |> ignore
        indexClient.CreateOrUpdateIndex searchIndex |> ignore

    let BLOB_DATA_SOURCE = "blob-transactions"

    // Create the data source of the JSON blobs of properties
    let createBlobDataSource connectionString indexConnection =
        let blobConnection =
            SearchIndexerDataSourceConnection(
                BLOB_DATA_SOURCE,
                SearchIndexerDataSourceType.AzureBlob,
                connectionString,
                SearchIndexerDataContainer PROPERTIES_INDEX)
        let searchIndexer = SearchIndexerClient (fst indexConnection, snd indexConnection)
        searchIndexer.DeleteDataSourceConnection blobConnection |> ignore
        searchIndexer.CreateDataSourceConnection blobConnection |> ignore

    // Create the indexer
    let createCsvIndexer indexConnection =
        let indexer =
            let indexingParameters =
                IndexingParameters(
                    IndexingParametersConfiguration =
                        IndexingParametersConfiguration(IndexedFileNameExtensions = ".csv"))
            indexingParameters.Configuration.["parsingMode"] <- "delimitedText"
            indexingParameters.Configuration.["firstLineContainsHeaders"] <- true

            SearchIndexer(
                "properties-csv-indexer",
                BLOB_DATA_SOURCE,
                PROPERTIES_INDEX,
                Schedule = IndexingSchedule(TimeSpan.FromHours 1.),
                Parameters = indexingParameters
            )
        let searchIndexer = SearchIndexerClient (fst indexConnection, snd indexConnection)
        searchIndexer.DeleteIndexer indexer |> ignore
        searchIndexer.CreateIndexer indexer |> ignore