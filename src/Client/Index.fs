module Index

open Elmish
open Fable.Remoting.Client
open Shared
open System

type SearchTextError =
    NoSearchText | InvalidPostcode
    member this.Description =
        match this with
        | NoSearchText -> "No search term supplied."
        | InvalidPostcode -> "This is an invalid postcode."
type LocationTab = ResultsGrid | Map

type SearchKind =
    FreeTextSearch | LocationSearch of LocationTab
    member this.Value = match this with FreeTextSearch -> 1 | LocationSearch _ -> 2
    member this.Description = match this with FreeTextSearch -> "Free Text" | LocationSearch _ -> "Post Code"

type SearchState = Searching | CannotSearch of SearchTextError | CanSearch of string

type Model =
    {
        SearchText : string
        SelectedSearchKind : SearchKind
        Properties : Deferred<PropertyResult list>
        SelectedProperty : PropertyResult option
        HasLoadedSomeData : bool
        Facets : Facets
        SelectedFacet: (string * string) option
    }
    member this.SearchTextError =
        if String.IsNullOrEmpty this.SearchText then
            Some NoSearchText
        else
            match this.SelectedSearchKind with
            | LocationSearch _ ->
                if Validation.isValidPostcode this.SearchText then None
                else Some InvalidPostcode
            | FreeTextSearch ->
                None

    member this.HasProperties =
        match this.Properties with
        | Resolved [] | InProgress | HasNotStarted -> false
        | Resolved _ -> true

type SearchMsg =
    | ByFreeText of AsyncOperation<string, SearchResponse>
    | ByLocation of AsyncOperation<string, Result<SearchResponse, string>>

type Msg =
    | SearchTextChanged of string
    | SearchKindSelected of SearchKind
    | DoPostcodeSearch of string
    | Search of SearchMsg
    | AppError of string
    | ViewProperty of PropertyResult
    | CloseProperty
    | SelectFacet of string * string
    | RemoveFacet

let searchApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<ISearchApi>

let init () =
    let facets =
        {
            Towns = []
            Localities = []
            Districts = []
            Counties = []
            Prices = []
        }

    let model =
        {
            SearchText = ""
            SelectedSearchKind = FreeTextSearch
            Properties = HasNotStarted
            SelectedProperty = None
            HasLoadedSomeData = false
            Facets = facets
            SelectedFacet = None
        }
    model, Cmd.none

let update msg model =
    match msg with
    | SearchTextChanged value ->
        { model with SearchText = value }, Cmd.none
    | SearchKindSelected kind ->
        match model.SelectedSearchKind, kind with
        | LocationSearch _, LocationSearch _
        | FreeTextSearch, FreeTextSearch ->
            { model with SelectedSearchKind = kind }, Cmd.none
        | LocationSearch _, FreeTextSearch
        | FreeTextSearch, LocationSearch _ ->
            // If we change search type completely, remove all loaded properties
            { model with
                SelectedSearchKind = kind
                Properties = HasNotStarted }, Cmd.none
    | Search (ByFreeText operation) ->
        match operation with
        | Start text ->
            { model with Properties = InProgress }, Cmd.OfAsync.either searchApi.FreeText { Text = text; Filter = None } (Complete >> ByFreeText >> Search) (string >> AppError)
        | Complete searchResponse ->
            { model with Properties = Resolved searchResponse.Results; HasLoadedSomeData = true; Facets = searchResponse.Facets }, Cmd.none
    | Search (ByLocation operation) ->
        match operation with
        | Start postcode ->
            { model with Properties = InProgress }, Cmd.OfAsync.either searchApi.ByLocation { Postcode = postcode; Filter = None } (Complete >> ByLocation >> Search) (string >> AppError)
        | Complete (Ok searchResponse) ->
            { model with Properties = Resolved searchResponse.Results; HasLoadedSomeData = true; Facets = searchResponse.Facets }, Cmd.none
        | Complete (Error message) ->
            model, Cmd.ofMsg (AppError message)
    | ViewProperty property ->
        { model with SelectedProperty = Some property }, Cmd.none
    | CloseProperty ->
        { model with SelectedProperty = None }, Cmd.none
    | DoPostcodeSearch postCode ->
        let commands = [
            SearchKindSelected (LocationSearch ResultsGrid)
            SearchTextChanged postCode
            Search (ByLocation (Start postCode))
        ]
        model, Cmd.batch (List.map Cmd.ofMsg commands)
    | AppError ex ->
        Browser.Dom.console.log ex
        model, Cmd.none
    | SelectFacet (facetKey, facetValue) ->
        let cmd =
            match model.SelectedSearchKind with
            | LocationSearch _ ->
                Cmd.OfAsync.either searchApi.ByLocation { Postcode = model.SearchText; Filter = Some (facetKey, facetValue) } (Complete >> ByLocation >> Search) (string >> AppError)
            | FreeTextSearch ->
                Cmd.OfAsync.either searchApi.FreeText { Text = model.SearchText; Filter = Some (facetKey, facetValue) } (Complete >> ByFreeText >> Search) (string >> AppError)

        { model with SelectedFacet = Some (facetKey, facetValue) }, cmd
    | RemoveFacet ->
        let cmd =
            match model.SelectedSearchKind with
            | LocationSearch _ ->
                Cmd.OfAsync.either searchApi.ByLocation { Postcode = model.SearchText; Filter = None } (Complete >> ByLocation >> Search) (string >> AppError)
            | FreeTextSearch ->
                Cmd.OfAsync.either searchApi.FreeText { Text = model.SearchText; Filter = None } (Complete >> ByFreeText >> Search) (string >> AppError)

        { model with SelectedFacet = None }, cmd


open Feliz
open Feliz.Bulma
open Feliz.PigeonMaps
open Feliz.Tippy
open Feliz.AgGrid
open Fable.Core.JsInterop

module Heading =
    let title =
        Bulma.title.h3 [
            Bulma.icon [
                prop.classes [ "has-text-info" ]
                prop.children [
                    Html.i [
                        prop.className "fas fa-home"
                    ]
                ]
            ]
            Html.span [
                Html.text " SAFE Search"
            ]
        ]
    let subtitle =
        Bulma.subtitle.h5 [
            Html.text "Find your unaffordable property in the UK!"
        ]

module Search =
    let searchInput (model:Model) dispatch =
        Bulma.control.div [
            control.hasIconsLeft
            match model.Properties with
            | IsLoading -> control.isLoading
            | IsNotLoading -> ()

            prop.children [
                Bulma.input.search [
                    prop.onChange (SearchTextChanged >> dispatch)

                    match model.SearchTextError, model.Properties with
                    | Some NoSearchText, _ ->
                        color.isPrimary
                        prop.placeholder "Enter your search term here."
                    | Some InvalidPostcode, _ ->
                        color.isDanger
                    | None, IsNotLoading ->
                        prop.valueOrDefault model.SearchText
                        color.isPrimary
                    | None, IsLoading ->
                        ()
                ]
                Bulma.icon [
                    icon.isSmall
                    icon.isLeft
                    prop.children [
                        Html.i [
                            prop.className "fas fa-search"
                        ]
                    ]
                ]
                match model.SearchTextError with
                | Some error ->
                    Bulma.help [
                        color.isDanger
                        prop.text error.Description
                    ]
                | None ->
                    ()
            ]
        ]
    let searchButton (model:Model) dispatch =
        Bulma.button.a [
            color.isPrimary
            match model.SearchTextError, model.Properties with
            | Some _, _ ->
                prop.disabled true
            | None, IsLoading ->
                button.isLoading
            | None, IsNotLoading ->
                match model.SelectedSearchKind with
                | FreeTextSearch -> prop.onClick(fun _ -> dispatch (Search (ByFreeText (Start model.SearchText))))
                | LocationSearch _ -> prop.onClick(fun _ -> dispatch (Search (ByLocation (Start model.SearchText))))
            prop.children [
                Bulma.icon [
                    prop.children [
                        Html.i [
                            prop.className "fas fa-search"
                        ]
                    ]
                ]
                Html.span [
                    Html.text "Search"
                ]
            ]
        ]

    let createSearchPanel model dispatch =
        Bulma.columns [
            Bulma.column [
                column.isThreeQuarters
                prop.children [ searchInput model dispatch ]
            ]
            Bulma.column [
                Bulma.level [
                    Bulma.levelItem [
                        Bulma.control.div [
                            control.hasIconsLeft
                            prop.children [
                                Bulma.select [
                                    prop.disabled (model.Properties = InProgress)
                                    prop.onChange (function
                                        | "1" -> dispatch (SearchKindSelected FreeTextSearch)
                                        | _ -> dispatch (SearchKindSelected (LocationSearch ResultsGrid))
                                    )
                                    prop.children [
                                        for kind in [ FreeTextSearch; LocationSearch ResultsGrid ] do
                                            Html.option [
                                                prop.text kind.Description
                                                prop.value kind.Value
                                            ]
                                    ]
                                    prop.valueOrDefault model.SelectedSearchKind.Value
                                ]
                                Bulma.icon [
                                    icon.isSmall
                                    icon.isLeft
                                    prop.children [
                                        Html.i [
                                            let iconName =
                                                match model.SelectedSearchKind with
                                                | FreeTextSearch -> "search"
                                                | LocationSearch _ -> "location-arrow"
                                            prop.className $"fas fa-{iconName}"
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                    Bulma.levelItem [
                        searchButton model dispatch
                    ]
                ]
            ]
        ]

let safeSearchNavBar =
    Bulma.navbar [
        color.isPrimary
        prop.children [
            Bulma.navbarBrand.a [
                prop.href "https://safe-stack.github.io/docs/"
                prop.children [
                    Bulma.navbarItem.div [
                        prop.children [
                            Html.img [ prop.src "favicon.png" ]
                            Html.text "SAFE Stack"
                        ]
                    ]
                ]
            ]
        ]
    ]

let FacetBox (label: string) facets selectedFacet dispatch =
    let fromPluralToSingular = function
        | "Counties" -> "County"
        | "Districts" -> "District"
        | "Localities" -> "Locality"
        | "Towns" -> "Town"
        | _ -> failwith "Invalid facet name"

    Bulma.box [
        Bulma.level [
            Bulma.levelLeft [
                Bulma.levelItem [
                    Bulma.label [
                        prop.style [ style.color.gray ]
                        prop.text label
                    ]
                ]
            ]
            Bulma.levelRight [
                Bulma.levelItem [
                    Html.i [
                        prop.style [ style.color.gray ]
                        prop.onClick (fun _ -> ())
                        prop.className ($"""fas fa-caret-{if true then "up" else "down"}""")
                    ]
                ]
            ]
        ]

        for (facet: string) in facets do
            let selected =
                selectedFacet
                |> Option.map ((=) (fromPluralToSingular label, facet))
                |> Option.defaultValue false

            Bulma.columns [
                columns.isCentered
                prop.children [
                    Bulma.column [
                        column.is1
                        prop.children [
                            Bulma.input.checkbox [
                                prop.isChecked selected
                                prop.onChange (fun (isChecked: bool) ->
                                    if isChecked then
                                        (fromPluralToSingular label, facet) |> SelectFacet |> dispatch
                                    else
                                        RemoveFacet |> dispatch
                                )
                            ]
                        ]
                    ]
                    Bulma.column [
                        Html.div [
                            prop.text (facet.ToLower())
                            prop.style [
                                if selected then style.fontWeight.bolder
                                style.textTransform.capitalize
                            ]
                        ]
                    ]
                ]
            ]
    ]

let facetsBoxes (facets: Facets) selectedFacets dispatch =
    Html.div [
        FacetBox "Counties" facets.Counties selectedFacets dispatch
        FacetBox "Districts" facets.Districts selectedFacets dispatch
        FacetBox "Localities" facets.Localities selectedFacets dispatch
        FacetBox "Towns" facets.Towns selectedFacets dispatch
        // PriceFacetBox facets.Prices dispatch
    ]

let resultsGrid dispatch searchKind (results:PropertyResult list) =
    Html.div [
        prop.className ThemeClass.Alpine
        prop.children [
            AgGrid.grid [
                AgGrid.rowData (List.toArray results)
                AgGrid.pagination true
                AgGrid.defaultColDef [
                    ColumnDef.resizable true
                    ColumnDef.sortable true
                    ColumnDef.editable (fun _ -> false)
                ]
                AgGrid.domLayout AutoHeight
                AgGrid.columnDefs [
                    ColumnDef.create<string> [
                        ColumnDef.onCellClicked (fun _ row -> dispatch (ViewProperty row))
                        ColumnDef.cellRendererFramework (fun _ _ -> Html.a [ Html.text "View" ])
                    ]
                    ColumnDef.create<DateTime> [
                        ColumnDef.filter Date
                        ColumnDef.headerName "Date"
                        ColumnDef.valueGetter (fun x -> x.DateOfTransfer)
                        ColumnDef.valueFormatter (fun x _ -> x.ToShortDateString())
                    ]
                    ColumnDef.create<int> [
                        ColumnDef.headerName "Price"
                        ColumnDef.filter Number
                        ColumnDef.valueGetter (fun x -> x.Price)
                        ColumnDef.columnType NumericColumn
                        ColumnDef.valueFormatter (fun value _ -> $"£{value?toLocaleString()}")
                    ]
                    ColumnDef.create<string> [
                        ColumnDef.filter Text
                        ColumnDef.headerName "Street"
                        ColumnDef.valueGetter (fun x -> x.Address.Street |> Option.toObj)
                    ]
                    ColumnDef.create<string> [
                        ColumnDef.filter Text
                        ColumnDef.headerName "Town"
                        ColumnDef.valueGetter (fun x -> x.Address.TownCity)
                    ]
                    ColumnDef.create<string> [
                        ColumnDef.filter Text
                        ColumnDef.headerName "County"
                        ColumnDef.valueGetter (fun x -> x.Address.County)
                    ]
                    ColumnDef.create<string> [
                        ColumnDef.filter Text
                        ColumnDef.headerName "Postcode"
                        match searchKind with
                        | FreeTextSearch ->
                            ColumnDef.onCellClicked (fun _ row -> dispatch (DoPostcodeSearch (Option.toObj row.Address.PostCode)))
                            ColumnDef.cellRendererFramework (fun _ x -> Html.a [ Html.text (Option.toObj x.Address.PostCode) ])
                        | LocationSearch _ ->
                            ColumnDef.valueGetter (fun x -> Option.toObj x.Address.PostCode)
                    ]
                ]
            ]
        ]
    ]

type MapSize = Full | Modal

let drawMap geoLocation mapSize properties =
    PigeonMaps.map [
        map.center (geoLocation.Lat, geoLocation.Long)
        map.zoom 16
        map.height (match mapSize with Full -> 700 | Modal -> 350)
        map.markers [
            let propertiesWithGeo = [
                for property in properties do
                    match property.Address.GeoLocation with
                    | Some geo -> geo, property
                    | None -> ()
            ]
            for geo, property in propertiesWithGeo do
                PigeonMaps.marker [
                    marker.anchor (geo.Lat, geo.Long)
                    marker.offsetLeft 15
                    marker.offsetTop 30
                    marker.render (fun _ -> [
                        Tippy.create [
                            Tippy.plugins [|
                                Plugins.followCursor
                                Plugins.animateFill
                                Plugins.inlinePositioning
                            |]
                            Tippy.placement Auto
                            Tippy.animateFill
                            Tippy.interactive
                            Tippy.content (
                                Html.div [
                                    prop.text $"{property.Address.Building}, {property.Address.Street |> Option.toObj} (£{property.Price?toLocaleString()})"
                                    prop.style [
                                        style.color.lightGreen
                                    ]
                                ]
                            )
                            prop.children [
                                Html.i [
                                    let icon =
                                        match property.BuildDetails.PropertyType with
                                        | Some (Terraced | Detached | SemiDetached) -> "home"
                                        | Some (FlatsMaisonettes | Other) -> "building"
                                        | None -> "map-marker"
                                    prop.className [ "fa"; $"fa-{icon}" ]
                                ]
                            ]
                        ]
                    ])
                ]
        ]
    ]

let modalView dispatch property =
    let makeLine text fields =
        Bulma.field.div [
            field.isHorizontal
            prop.children [
                Bulma.fieldLabel [
                    fieldLabel.isNormal
                    prop.text (text:string)
                ]
                Bulma.fieldBody [
                    for (field:string) in fields do
                        Bulma.field.div [
                            Bulma.control.p [
                                Bulma.input.text [
                                    prop.readOnly true
                                    prop.value field
                                ]
                            ]
                        ]
                ]
            ]
        ]

    Bulma.modal [
        modal.isActive
        prop.children [
            Bulma.modalBackground [
                prop.onClick (fun _ -> dispatch CloseProperty)
            ]
            Bulma.modalContent [
                Bulma.box [
                    makeLine "Street" [ $"{property.Address.Building}, {property.Address.Street |> Option.toObj}" ]
                    makeLine "Town" [ property.Address.District; property.Address.County; (Option.toObj property.Address.PostCode) ]
                    makeLine "Price" [ $"£{property.Price?toLocaleString()}" ]
                    makeLine "Date" [ property.DateOfTransfer.ToShortDateString() ]
                    makeLine "Build" [ property.BuildDetails.Build.Description; property.BuildDetails.Contract.Description; property.BuildDetails.PropertyType |> Option.map(fun p -> p.Description) |> Option.toObj ]

                    match property.Address.GeoLocation with
                    | Some geoLocation ->
                        drawMap geoLocation Modal [ property ]
                    | None ->
                        ()
                ]
            ]
            Bulma.modalClose [
                modalClose.isLarge
                prop.ariaLabel "close"
                prop.onClick (fun _ -> dispatch CloseProperty)
            ]
        ]
    ]

let view (model:Model) dispatch =
    Html.div [
        safeSearchNavBar
        Bulma.section [
            if not model.HasProperties && not model.HasLoadedSomeData then section.isLarge
            prop.children [
                Bulma.container [
                    Heading.title
                    Heading.subtitle
                    Search.createSearchPanel model dispatch
                    match model.Properties with
                    | Resolved (NonEmpty results) ->
                        Bulma.columns [
                            Bulma.column [
                                column.isOneFifth
                                prop.children [
                                    facetsBoxes model.Facets model.SelectedFacet dispatch
                                ]
                            ]
                            Bulma.column [
                                Bulma.container [
                                    match model.SelectedSearchKind with
                                    | LocationSearch locationTab ->
                                        let makeTab searchKind (text:string) faIcon =
                                            Bulma.tab [
                                                if (searchKind = locationTab) then tab.isActive
                                                prop.children [
                                                    Html.a [
                                                        prop.onClick (fun _ -> dispatch (SearchKindSelected (LocationSearch searchKind)))
                                                        prop.children [
                                                            Bulma.icon [
                                                                icon.isSmall
                                                                prop.children [
                                                                    Html.i [ prop.className $"fas fa-{faIcon}" ]
                                                                ]
                                                            ]
                                                            Html.text text
                                                        ]
                                                    ]
                                                ]
                                            ]
                                        Bulma.tabs [
                                            Html.ul [
                                                makeTab ResultsGrid "Results Grid" "table"
                                                makeTab Map "Map" "map"
                                            ]
                                        ]
                                        match locationTab with
                                        | ResultsGrid ->
                                            resultsGrid dispatch (LocationSearch ResultsGrid) results
                                        | Map ->
                                            match results |> List.tryPick (fun r -> r.Address.GeoLocation) with
                                            | Some geoLocation ->
                                                Bulma.box [
                                                    drawMap geoLocation Full results
                                                ]
                                            | None ->
                                                ()
                                    | FreeTextSearch ->
                                        resultsGrid dispatch FreeTextSearch results
                                ]
                            ]
                        ]
                    | _ ->
                        ()
                ]
                yield!
                    model.SelectedProperty
                    |> Option.map (modalView dispatch)
                    |> Option.toList
            ]
        ]
    ]