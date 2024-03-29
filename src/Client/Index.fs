module Index

open Elmish
open Fable.Remoting.Client
open Shared
open System

type SearchTextError =
    | NoSearchText
    | InvalidPostcode

    member this.Description =
        match this with
        | NoSearchText -> "No search term supplied."
        | InvalidPostcode -> "This is an invalid postcode."

type LocationTab =
    | ResultsGrid
    | Map
    | Crime of Geo

type SearchKind =
    | FreeTextSearch
    | LocationSearch of LocationTab

    member this.Value =
        match this with
        | FreeTextSearch -> 1
        | LocationSearch _ -> 2

    member this.Description =
        match this with
        | FreeTextSearch -> "Free Text"
        | LocationSearch _ -> "Post Code"

type SearchState =
    | Searching
    | CannotSearch of SearchTextError
    | CanSearch of string

type Suggestions = { Visible: bool; Results: string array }

type Model = {
    SearchText: string
    SelectedSearchKind: SearchKind
    Properties: Deferred<PropertyResult list>
    SelectedProperty: PropertyResult option
    HasLoadedSomeData: bool
    Facets: Facets
    SelectedFacets: (string * string) list
    FilterMenuOpen: bool
    CrimeIncidents: Deferred<CrimeResponse array>
    Suggestions: Suggestions
} with

    member this.SearchTextError =
        if String.IsNullOrEmpty this.SearchText then
            Some NoSearchText
        else
            match this.SelectedSearchKind with
            | LocationSearch _ ->
                if Validation.isValidPostcode this.SearchText then
                    None
                else
                    Some InvalidPostcode
            | FreeTextSearch -> None

    member this.HasProperties =
        match this.Properties with
        | Resolved []
        | InProgress
        | HasNotStarted -> false
        | Resolved _ -> true

type SearchMsg =
    | ByFreeText of AsyncOperation<string, SearchResponse>
    | ByLocation of AsyncOperation<string, Result<SearchResponse, string>>

type Toggle =
    | Open
    | Close

    static member Visibility =
        function
        | Open -> true
        | Close -> false

type SuggestionsMsg =
    | ToggleVisibility of Toggle
    | GotSuggestions of SuggestResponse

type Msg =
    | SearchTextChanged of string
    | SearchKindSelected of SearchKind
    | DoPostcodeSearch of string
    | Search of SearchMsg
    | AppError of string
    | ViewProperty of PropertyResult
    | CloseProperty
    | SelectFacet of string * string
    | RemoveFacet of string * string
    | ToggleFilterMenu of Toggle
    | LoadCrimeIncidents of CrimeResponse array
    | Suggestions of SuggestionsMsg

type Key =
    | Enter
    | ArrowUp
    | ArrowDown
    | Escape

    static member Pressed =
        function
        | "Enter" -> Some Enter
        | "ArrowUp" -> Some ArrowUp
        | "ArrowDown" -> Some ArrowDown
        | "Escape" -> Some Escape
        | _ -> None

let searchApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<ISearchApi>

let initModel () =
    let facets = {
        Towns = []
        Localities = []
        Districts = []
        Counties = []
        Prices = []
    }

    {
        SearchText = ""
        SelectedSearchKind = FreeTextSearch
        Properties = HasNotStarted
        SelectedProperty = None
        HasLoadedSomeData = false
        Facets = facets
        SelectedFacets = []
        FilterMenuOpen = false
        CrimeIncidents = HasNotStarted
        Suggestions = { Visible = false; Results = [||] }
    }

let init () = initModel (), Cmd.none

let alreadySelected suggestions value = suggestions |> Array.exists ((=) value)

let update msg model =
    match msg with
    | SearchTextChanged value ->
        let updatedSearchResetSuggestions = {
            model with
                SearchText = value
                Suggestions = { Visible = false; Results = [||] }
        }

        if value = "" then
            updatedSearchResetSuggestions, Cmd.none
        elif value |> alreadySelected model.Suggestions.Results then
            {
                model with
                    SearchText = value
                    Suggestions = {
                        model.Suggestions with
                            Visible = false
                    }
            },
            Cmd.none
        else
            { model with SearchText = value },
            Cmd.OfAsync.perform searchApi.GetSuggestions value (GotSuggestions >> Suggestions)
    | Suggestions msg ->
        match msg with
        | ToggleVisibility toggle ->
            match model.SelectedSearchKind with
            | FreeTextSearch ->
                let toggleVisibility visibility = {
                    model with
                        Suggestions = {
                            model.Suggestions with
                                Visible = visibility && model.Suggestions.Results |> Array.isEmpty |> not
                        }
                }

                let model =
                    match toggle with
                    | Open when model.SearchText = "" -> toggleVisibility false
                    | Open -> toggleVisibility true
                    | Close -> toggleVisibility false

                model, Cmd.none
            | LocationSearch _ ->
                {
                    model with
                        Suggestions = { Visible = false; Results = [||] }
                },
                Cmd.none
        | GotSuggestions response ->
            {
                model with
                    Suggestions = {
                        Visible = response.Suggestions |> Array.isEmpty |> not
                        Results = response.Suggestions
                    }
            },
            Cmd.none
    | SearchKindSelected kind ->
        match model.SelectedSearchKind, kind with
        | LocationSearch _, LocationSearch tab ->
            match tab with
            | Crime geo ->
                {
                    model with
                        SelectedSearchKind = kind
                        CrimeIncidents = InProgress
                },
                Cmd.OfAsync.either searchApi.GetCrimes geo LoadCrimeIncidents (string >> AppError)
            | _ -> { model with SelectedSearchKind = kind }, Cmd.none
        | FreeTextSearch, FreeTextSearch -> model, Cmd.none
        | LocationSearch _, FreeTextSearch
        | FreeTextSearch, LocationSearch _ ->
            // If we change search type completely, remove all loaded properties
            {
                model with
                    SearchText = ""
                    SelectedSearchKind = kind
                    Suggestions = {
                        model.Suggestions with
                            Visible = false
                    }
                    Properties = HasNotStarted
            },
            Cmd.none
    | Search(ByFreeText operation) ->
        match operation with
        | Start text ->
            {
                model with
                    Properties = InProgress
                    SelectedFacets = []
                    Suggestions = {
                        model.Suggestions with
                            Visible = false
                    }
            },
            Cmd.OfAsync.either
                searchApi.FreeText
                { Text = text; Filters = [] }
                (Complete >> ByFreeText >> Search)
                (string >> AppError)
        | Complete searchResponse ->
            {
                model with
                    Properties = Resolved searchResponse.Results
                    HasLoadedSomeData = true
                    Facets = searchResponse.Facets
            },
            Cmd.none
    | Search(ByLocation operation) ->
        match operation with
        | Start postcode ->
            {
                model with
                    Properties = InProgress
                    SelectedFacets = []
                    SelectedSearchKind = LocationSearch ResultsGrid
            },
            Cmd.OfAsync.either
                searchApi.ByLocation
                { Postcode = postcode; Filters = [] }
                (Complete >> ByLocation >> Search)
                (string >> AppError)
        | Complete(Ok searchResponse) ->
            {
                model with
                    Properties = Resolved searchResponse.Results
                    HasLoadedSomeData = true
                    Facets = searchResponse.Facets
            },
            Cmd.none
        | Complete(Error message) ->
            {
                initModel () with
                    SearchText = model.SearchText
                    SelectedSearchKind = LocationSearch ResultsGrid
            },
            Cmd.ofMsg (AppError message)
    | ViewProperty property ->
        {
            model with
                SelectedProperty = Some property
        },
        Cmd.none
    | CloseProperty -> { model with SelectedProperty = None }, Cmd.none
    | DoPostcodeSearch postCode ->
        let commands = [
            SearchKindSelected(LocationSearch ResultsGrid)
            SearchTextChanged postCode
            Search(ByLocation(Start postCode))
        ]

        model, Cmd.batch (List.map Cmd.ofMsg commands)
    | AppError ex ->
        Browser.Dom.console.log ex
        model, Cmd.none
    | SelectFacet(facetKey, facetValue) ->
        let selectedFacets =
            match model.SelectedFacets with
            | [] -> [ (facetKey, facetValue) ]
            | tail -> ((facetKey, facetValue) :: tail)

        let cmd =
            match model.SelectedSearchKind with
            | LocationSearch _ ->
                Cmd.OfAsync.either
                    searchApi.ByLocation
                    {
                        Postcode = model.SearchText
                        Filters = selectedFacets
                    }
                    (Complete >> ByLocation >> Search)
                    (string >> AppError)
            | FreeTextSearch ->
                Cmd.OfAsync.either
                    searchApi.FreeText
                    {
                        Text = model.SearchText
                        Filters = selectedFacets
                    }
                    (Complete >> ByFreeText >> Search)
                    (string >> AppError)

        {
            model with
                SelectedFacets = selectedFacets
        },
        cmd
    | RemoveFacet(facetKey, facetValue) ->
        let updatedFacets =
            model.SelectedFacets |> List.filter ((<>) (facetKey, facetValue))

        let cmd =
            match model.SelectedSearchKind with
            | LocationSearch _ ->
                Cmd.OfAsync.either
                    searchApi.ByLocation
                    {
                        Postcode = model.SearchText
                        Filters = updatedFacets
                    }
                    (Complete >> ByLocation >> Search)
                    (string >> AppError)
            | FreeTextSearch ->
                Cmd.OfAsync.either
                    searchApi.FreeText
                    {
                        Text = model.SearchText
                        Filters = updatedFacets
                    }
                    (Complete >> ByFreeText >> Search)
                    (string >> AppError)

        {
            model with
                SelectedFacets = updatedFacets
        },
        cmd
    | ToggleFilterMenu toggle ->
        let model =
            match toggle with
            | Open -> { model with FilterMenuOpen = true }
            | Close -> { model with FilterMenuOpen = false }

        model, Cmd.none
    | LoadCrimeIncidents crimeIncidents ->
        {
            model with
                CrimeIncidents = Resolved crimeIncidents
        },
        Cmd.none

open Feliz
open Feliz.Bulma
open Feliz.PigeonMaps
open Feliz.Tippy
open Feliz.AgGrid
open Fable.Core.JsInterop
open Feliz.Recharts
open Feliz.ReactLoadingSkeleton
open Feliz.UseElmish

importAll "./styles.scss"

module Heading =
    let title =
        Bulma.title.h3 [
            Bulma.icon [
                prop.classes [ "has-text-info" ]
                prop.children [ Html.i [ prop.className "fas fa-home" ] ]
            ]
            Html.span [ Html.text " SAFE Search" ]
        ]

    let subtitle =
        Bulma.subtitle.h5 [ Html.text "Find your unaffordable property in the UK!" ]

module Facets =
    let fromPluralToSingular =
        function
        | "Counties" -> "County"
        | "Districts" -> "District"
        | "Localities" -> "Locality"
        | "Towns" -> "Town"
        | facet -> failwithf $"Invalid facet name: {facet}"

    let (|NoResults|NotFiltered|Filtered|) (label, facets, selectedFacets) =
        if
            facets
            |> List.exists (fun facet -> selectedFacets |> List.exists ((=) (fromPluralToSingular label, facet)))
        then
            Filtered
        elif facets |> List.isEmpty then
            NoResults
        else
            NotFiltered

    let panelColour =
        function
        | NoResults -> color.isDanger
        | NotFiltered -> color.isInfo
        | Filtered -> color.isPrimary

    let facetBox (label: string) facets selectedFacets dispatch =
        Bulma.panel [
            panelColour (label, facets, selectedFacets)
            prop.style [ style.borderRadius 0 ]
            prop.children [
                Bulma.panelHeading [ prop.style [ style.borderRadius 0 ]; prop.text label ]
                for facet in facets do
                    let facetKeyValue = fromPluralToSingular label, facet
                    let isSelected = selectedFacets |> List.exists ((=) facetKeyValue)

                    Bulma.panelBlock.div [
                        Bulma.columns [
                            columns.isMobile
                            columns.isVCentered
                            prop.style [ style.width (length.percent 100); style.paddingLeft 10 ]
                            prop.children [
                                Bulma.column [
                                    column.is1
                                    prop.children [
                                        Bulma.input.checkbox [
                                            prop.isChecked isSelected
                                            prop.onChange (fun isChecked ->
                                                if isChecked then
                                                    facetKeyValue |> SelectFacet |> dispatch
                                                else
                                                    facetKeyValue |> RemoveFacet |> dispatch)
                                        ]
                                    ]
                                ]
                                Bulma.column [
                                    prop.text (facet.ToLower())
                                    prop.style [
                                        style.textOverflow.ellipsis
                                        if isSelected then
                                            style.fontWeight.bolder
                                        style.textTransform.capitalize
                                    ]
                                ]
                            ]
                        ]
                    ]
                if facets.IsEmpty then
                    Bulma.panelBlock.div [ prop.text "No results" ]
            ]
        ]

    let facetBoxes (facets: Facets) selectedFacets dispatch =
        Html.div [
            facetBox "Counties" facets.Counties selectedFacets dispatch
            facetBox "Districts" facets.Districts selectedFacets dispatch
            facetBox "Localities" facets.Localities selectedFacets dispatch
            facetBox "Towns" facets.Towns selectedFacets dispatch
        ]

    let facetMenu menuVisible facets selectedFacets dispatch =
        QuickView.quickview [
            if menuVisible then
                helpers.isHiddenDesktop
                quickview.isActive
            prop.children [
                QuickView.header [
                    Html.div "Filters"
                    Bulma.delete [ prop.onClick (fun _ -> Close |> ToggleFilterMenu |> dispatch) ]
                ]
                QuickView.body [ facetBoxes facets selectedFacets dispatch ]
            ]
        ]

module Map =
    type MapSize =
        | Full
        | Modal

    let drawMap geoLocation mapSize properties =
        PigeonMaps.map [
            map.center (geoLocation.Lat, geoLocation.Long)
            map.zoom 16
            map.height (
                match mapSize with
                | Full -> 700
                | Modal -> 350
            )
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
                                        prop.text
                                            $"{property.Address.Building}, {property.Address.Street |> Option.toObj} (£{property.Price?toLocaleString()})"
                                        prop.style [ style.fontSize 12; style.width 150; style.color.lightGreen ]
                                    ]
                                )
                                prop.children [
                                    Html.i [
                                        let icon =
                                            match property.BuildDetails.PropertyType with
                                            | Some(Terraced | Detached | SemiDetached) -> "home"
                                            | Some(FlatsMaisonettes | Other) -> "building"
                                            | None -> "map-marker"

                                        prop.className [ "fa"; $"fa-{icon}" ]
                                    ]
                                ]
                            ]
                        ])
                    ]
            ]
        ]

module Search =
    module Debouncer =
        type private DebounceState<'a> = {
            Value: 'a
            OnDone: 'a -> unit
            Delay: int
        }

        type private Msg<'a> =
            | ValueChanged of 'a
            | Debounced of 'a

        let private init value onDone delay =
            {
                Value = value
                OnDone = onDone
                Delay = delay
            },
            []

        let private update msg model =
            match msg with
            | Debounced thenValue ->
                if model.Value = thenValue then
                    model.OnDone thenValue

                model, Cmd.none
            | ValueChanged value ->
                let asyncMsg value = async {
                    do! Async.Sleep model.Delay
                    return value
                }

                { model with Value = value }, Cmd.OfAsync.perform asyncMsg value Debounced

        let useDebouncer value onDone delay =
            let current, dispatch =
                React.useElmish (init value onDone delay, update, [||])

            current.Value, (ValueChanged >> dispatch)

    let suggestionsBox suggestions highlightedIndex onSuggestionClicked onSuggestionHighlightChanged =
        Bulma.box [
            prop.tabIndex 1
            prop.style [
                style.position.absolute
                style.padding 0
                style.width (length.percent 100)
                style.marginTop 5
                style.border (1, borderStyle.solid, "#e6e6e6")
                style.zIndex 10
                style.borderRadius 5
            ]
            prop.children [
                yield!
                    suggestions.Results
                    |> Array.mapi (fun i suggestion ->
                        Html.p [
                            // onClick fires after onBlur so using onMouseDown so event fires before suggestions are removed from the DOM
                            prop.onMouseDown (fun _ -> onSuggestionClicked suggestion)
                            prop.onMouseEnter (fun _ -> onSuggestionHighlightChanged (Some i))
                            prop.style [
                                style.padding 10
                                style.textTransform.capitalize
                                style.cursor.pointer
                                match highlightedIndex with
                                | Some idx when i = idx ->
                                    style.backgroundColor "#00d1b2"
                                    style.color.white
                                | _ -> ()
                            ]
                            prop.text (suggestion.ToLower())
                        ])
            ]
        ]

    [<ReactComponent>]
    let AutoCompleteSearch (model: Model) dispatch =
        let currentValue, onChange =
            Debouncer.useDebouncer model.SearchText (SearchTextChanged >> dispatch) 300

        let highlightedIndex, setHighlightedIndex = React.useState None

        let showSuggestions () =
            if not model.Suggestions.Visible then
                Open |> ToggleVisibility |> Suggestions |> dispatch

        let hideSuggestions () =
            if model.Suggestions.Visible then
                Close |> ToggleVisibility |> Suggestions |> dispatch

        let startSearch searchTerm =
            searchTerm |> Start |> ByFreeText |> Search |> dispatch

        Html.div [
            prop.style [ style.position.relative ]
            prop.children [
                Bulma.control.div [
                    control.hasIconsLeft
                    match model.Properties with
                    | IsLoading -> control.isLoading
                    | IsNotLoading -> ()
                    prop.children [
                        Bulma.input.search [
                            prop.className "move"
                            prop.tabIndex 1
                            prop.onChange onChange
                            prop.value currentValue
                            prop.style [ style.textTransform.capitalize ]
                            prop.onClick (fun _ -> showSuggestions ())
                            prop.onBlur (fun _ -> hideSuggestions ())
                            match model.SearchTextError, model.Properties with
                            | Some NoSearchText, _ ->
                                color.isPrimary
                                prop.placeholder "Enter your search term here."
                            | None, IsNotLoading ->
                                prop.valueOrDefault currentValue
                                color.isPrimary
                            | _, _ -> ()
                            prop.onKeyDown (fun e ->
                                let results = model.Suggestions.Results

                                match Key.Pressed e.key with
                                | Some ArrowUp ->
                                    e.preventDefault ()

                                    let newIndex =
                                        match highlightedIndex with
                                        | None -> Some(results.Length - 1)
                                        | Some idx when idx > results.Length -> Some(results.Length - 1)
                                        | Some idx when not model.Suggestions.Visible -> Some idx
                                        | Some idx when idx <= 0 -> None
                                        | Some idx -> Some(idx - 1)

                                    setHighlightedIndex newIndex
                                    showSuggestions ()
                                | Some ArrowDown ->
                                    e.preventDefault ()

                                    let newIndex =
                                        match highlightedIndex with
                                        | None -> Some 0
                                        | Some idx when idx > results.Length -> Some 0
                                        | Some idx when not model.Suggestions.Visible -> Some idx
                                        | Some idx when idx = results.Length - 1 -> None
                                        | Some idx -> Some(idx + 1)

                                    setHighlightedIndex newIndex
                                    showSuggestions ()
                                | Some Escape ->
                                    if highlightedIndex.IsSome then
                                        e.preventDefault ()
                                        setHighlightedIndex None
                                    else
                                        hideSuggestions ()
                                | Some Enter ->
                                    e.preventDefault ()

                                    match model.Suggestions.Visible, highlightedIndex with
                                    | true, Some idx when idx < results.Length ->
                                        let highlightedSuggestion = results.[idx]
                                        onChange highlightedSuggestion
                                        startSearch highlightedSuggestion
                                    | _ -> startSearch currentValue
                                | None -> showSuggestions ())
                        ]
                        Bulma.icon [
                            icon.isSmall
                            icon.isLeft
                            prop.children [ Html.i [ prop.className "fas fa-search" ] ]
                        ]
                        match model.SearchTextError with
                        | Some error -> Bulma.help [ color.isDanger; prop.text error.Description ]
                        | None -> ()
                    ]
                ]
                if model.Suggestions.Visible then
                    suggestionsBox
                        model.Suggestions
                        highlightedIndex
                        (fun suggestion ->
                            onChange suggestion
                            startSearch suggestion)
                        setHighlightedIndex
            ]
        ]

    let postCodeSearchInput model dispatch =
        Bulma.control.div [
            control.hasIconsLeft
            match model.Properties with
            | IsLoading -> control.isLoading
            | IsNotLoading -> ()
            prop.children [
                Bulma.input.search [
                    prop.tabIndex 1
                    prop.onChange (SearchTextChanged >> dispatch)
                    prop.onKeyPress (fun e ->
                        match Key.Pressed e.key with
                        | Some Enter ->
                            if Validation.isValidPostcode model.SearchText then
                                model.SearchText |> Start |> ByLocation |> Search |> dispatch
                        | _ -> ())
                    prop.value model.SearchText
                    prop.style [ style.textTransform.uppercase ]
                    match model.SearchTextError, model.Properties with
                    | Some NoSearchText, _ ->
                        color.isPrimary
                        prop.placeholder "Enter your search term here."
                    | Some InvalidPostcode, _ -> color.isDanger
                    | None, IsNotLoading ->
                        prop.valueOrDefault model.SearchText
                        color.isPrimary
                    | None, IsLoading -> ()
                ]
                Bulma.icon [
                    icon.isSmall
                    icon.isLeft
                    prop.children [ Html.i [ prop.className "fas fa-search" ] ]
                ]
                match model.SearchTextError with
                | Some error -> Bulma.help [ color.isDanger; prop.text error.Description ]
                | None -> ()
            ]
        ]


    let searchButton (model: Model) dispatch =
        Bulma.button.a [
            button.isFullWidth
            prop.tabIndex 3
            color.isPrimary
            match model.SearchTextError, model.Properties with
            | Some _, _ -> prop.disabled true
            | None, IsLoading -> button.isLoading
            | None, IsNotLoading ->
                match model.SelectedSearchKind with
                | FreeTextSearch -> prop.onClick (fun _ -> dispatch (Search(ByFreeText(Start model.SearchText))))
                | LocationSearch _ -> prop.onClick (fun _ -> dispatch (Search(ByLocation(Start model.SearchText))))
            prop.onKeyPress (fun e ->
                match Key.Pressed e.key with
                | Some Enter ->
                    if Validation.isValidPostcode model.SearchText then
                        model.SearchText |> Start |> ByFreeText |> Search |> dispatch
                | _ -> ())
            prop.children [
                Bulma.icon [ prop.children [ Html.i [ prop.className "fas fa-search" ] ] ]
                Html.span [ Html.text "Search" ]
            ]
        ]

    let filterButton (model: Model) dispatch =
        Bulma.button.a [
            button.isFullWidth
            color.isPrimary
            prop.tabIndex 4
            match model.SearchTextError, model.Properties with
            | Some _, _ -> prop.disabled true
            | None, IsLoading -> button.isLoading
            | None, IsNotLoading -> prop.onClick (fun _ -> Open |> ToggleFilterMenu |> dispatch)
            prop.onKeyPress (fun e ->
                match Key.Pressed e.key with
                | Some Enter -> Open |> ToggleFilterMenu |> dispatch
                | _ -> ())
            prop.children [
                Bulma.icon [ prop.children [ Html.i [ prop.className "fas fa-filter" ] ] ]
                Html.span [ Html.text "Filter" ]
            ]
        ]

    let createSearchPanel model dispatch =
        Bulma.columns [
            Bulma.column [
                column.isThreeFifths
                prop.children [
                    match model.SelectedSearchKind with
                    | FreeTextSearch -> AutoCompleteSearch model dispatch
                    | LocationSearch _ -> postCodeSearchInput model dispatch
                ]
            ]
            Bulma.column [
                column.isOneFifth
                prop.children [
                    Bulma.control.div [
                        control.hasIconsLeft
                        prop.children [
                            Bulma.select [
                                prop.tabIndex 2
                                select.isFullWidth
                                prop.style [ style.width (length.percent 100) ]
                                prop.disabled (model.Properties = InProgress)
                                prop.onChange (function
                                    | "1" -> dispatch (SearchKindSelected FreeTextSearch)
                                    | _ -> dispatch (SearchKindSelected(LocationSearch ResultsGrid)))
                                prop.children [
                                    for kind in [ FreeTextSearch; LocationSearch ResultsGrid ] do
                                        Html.option [ prop.text kind.Description; prop.value kind.Value ]
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
            ]
            Bulma.column [ column.isOneFifth; prop.children [ searchButton model dispatch ] ]
            Bulma.column [ helpers.isHiddenDesktop; prop.children [ filterButton model dispatch ] ]
        ]

    let resultsGrid dispatch searchKind (results: PropertyResult list) =
        Html.div [
            prop.className ThemeClass.Alpine
            prop.children [
                AgGrid.grid [
                    AgGrid.rowData (List.toArray results)
                    AgGrid.pagination true
                    AgGrid.defaultColDef [
                        ColumnDef.resizable true
                        ColumnDef.sortable true
                        ColumnDef.editable (fun _ _ -> false)
                    ]
                    AgGrid.domLayout AutoHeight
                    AgGrid.columnDefs [
                        ColumnDef.create<string> [
                            ColumnDef.onCellClicked (fun _ row -> dispatch (ViewProperty row))
                            ColumnDef.cellRenderer (fun _ _ -> Html.a [ Html.text "View" ])
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
                            ColumnDef.valueFormatter (fun value _ -> $"£{value?toLocaleString ()}")
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
                                ColumnDef.onCellClicked (fun _ row ->
                                    dispatch (DoPostcodeSearch(Option.toObj row.Address.PostCode)))

                                ColumnDef.cellRenderer (fun _ x ->
                                    Html.a [ Html.text (Option.toObj x.Address.PostCode) ])
                            | LocationSearch _ -> ColumnDef.valueGetter (fun x -> Option.toObj x.Address.PostCode)
                        ]
                    ]
                ]
            ]
        ]

    let crimeChart (data: CrimeResponse array) =
        Recharts.barChart [
            barChart.layout.vertical
            barChart.data data
            barChart.width 600
            barChart.height 500
            barChart.children [
                Recharts.cartesianGrid [ cartesianGrid.strokeDasharray (4, 4) ]
                Recharts.xAxis [ xAxis.number ]
                Recharts.yAxis [ yAxis.dataKey (fun point -> point.Crime); yAxis.width 200; yAxis.category ]
                Recharts.tooltip []
                Recharts.bar [
                    bar.legendType.star
                    bar.isAnimationActive true
                    bar.animationEasing.ease
                    bar.dataKey (fun x -> x.Incidents)
                    bar.fill "#3298dc"
                ]
            ]
        ]

    let loadingSkeleton =
        Bulma.columns [
            Bulma.column [
                helpers.isHiddenTouch
                column.isOneQuarter
                prop.children [ Skeleton.skeleton [ Skeleton.count 3; Skeleton.height 500 ] ]
            ]
            Bulma.column [
                Bulma.container [ Skeleton.skeleton [ Skeleton.count 15; Skeleton.height 50 ] ]
            ]
        ]

    let renderLocationSearch dispatch locationTab results crimeIncidents =
        let makeTab searchKind (text: string) faIcon =
            Bulma.tab [
                if searchKind = locationTab then
                    tab.isActive
                prop.children [
                    Html.a [
                        prop.onClick (fun _ -> dispatch (SearchKindSelected(LocationSearch searchKind)))
                        prop.children [
                            Bulma.icon [ icon.isSmall; prop.children [ Html.i [ prop.className $"fas fa-{faIcon}" ] ] ]
                            Html.text text
                        ]
                    ]
                ]
            ]

        let geoLocationOpt = results |> List.tryPick (fun x -> x.Address.GeoLocation)

        React.fragment [
            Bulma.tabs [
                Html.ul [
                    makeTab ResultsGrid "Results Grid" "table"
                    makeTab Map "Map" "map"
                    yield!
                        geoLocationOpt
                        |> Option.toList
                        |> List.map (fun location -> makeTab (Crime location) "Crime" "mask")
                ]
            ]
            match locationTab with
            | ResultsGrid -> resultsGrid dispatch (LocationSearch ResultsGrid) results
            | Map ->
                match geoLocationOpt with
                | Some geoLocation -> Bulma.box [ Map.drawMap geoLocation Map.Full results ]
                | None -> ()
            | Crime _ ->
                Bulma.box [
                    prop.style [
                        style.display.flex
                        style.justifyContent.center
                        style.alignItems.center
                        style.height 520
                    ]
                    prop.children [
                        match crimeIncidents with
                        | Resolved incidents ->
                            let cleanData =
                                incidents
                                |> Array.map (fun c -> {
                                    c with
                                        Crime = c.Crime.[0..0].ToUpper() + c.Crime.[1..].Replace('-', ' ')
                                })

                            crimeChart cleanData
                        | _ -> Interop.reactApi.createElement (import "Gauge" "css-spinners-react", createObj [])
                    ]
                ]
        ]

    let createSearchResults model dispatch =
        match model.Properties with
        | Resolved(NonEmpty results) ->
            Bulma.columns [
                Bulma.column [
                    helpers.isHiddenTouch
                    column.isOneQuarter
                    prop.children [ Facets.facetBoxes model.Facets model.SelectedFacets dispatch ]
                ]
                Bulma.column [
                    Bulma.container [
                        match model.SelectedSearchKind with
                        | LocationSearch locationTab ->
                            renderLocationSearch dispatch locationTab results model.CrimeIncidents
                        | FreeTextSearch -> resultsGrid dispatch FreeTextSearch results
                    ]
                ]
            ]
        | InProgress -> loadingSkeleton
        | _ -> Html.none

let safeSearchNavBar =
    Bulma.navbar [
        color.isPrimary
        prop.children [
            Bulma.navbarBrand.a [
                prop.href "https://safe-stack.github.io/docs/"
                prop.children [
                    Bulma.navbarItem.div [
                        prop.children [ Html.img [ prop.src "favicon.png" ]; Html.text "SAFE Stack" ]
                    ]
                ]
            ]
        ]
    ]

let modalView dispatch property =
    let makeLine text fields =
        Bulma.field.div [
            field.isHorizontal
            prop.children [
                Bulma.fieldLabel [ fieldLabel.isNormal; prop.text (text: string) ]
                Bulma.fieldBody [
                    for field: string in fields do
                        Bulma.field.div [
                            Bulma.control.p [ Bulma.input.text [ prop.readOnly true; prop.value field ] ]
                        ]
                ]
            ]
        ]

    Bulma.modal [
        modal.isActive
        prop.children [
            Bulma.modalBackground [ prop.onClick (fun _ -> dispatch CloseProperty) ]
            Bulma.modalContent [
                Bulma.box [
                    makeLine "Street" [ $"{property.Address.Building}, {property.Address.Street |> Option.toObj}" ]
                    makeLine "Town" [
                        property.Address.District
                        property.Address.County
                        (Option.toObj property.Address.PostCode)
                    ]
                    makeLine "Price" [ $"£{property.Price?toLocaleString()}" ]
                    makeLine "Date" [ property.DateOfTransfer.ToShortDateString() ]
                    makeLine "Build" [
                        property.BuildDetails.Build.Description
                        property.BuildDetails.Contract.Description
                        property.BuildDetails.PropertyType
                        |> Option.map (fun p -> p.Description)
                        |> Option.toObj
                    ]

                    match property.Address.GeoLocation with
                    | Some geoLocation -> Map.drawMap geoLocation Map.Modal [ property ]
                    | None -> ()
                ]
            ]
            Bulma.modalClose [
                modalClose.isLarge
                prop.ariaLabel "close"
                prop.onClick (fun _ -> dispatch CloseProperty)
            ]
        ]
    ]


let view (model: Model) dispatch =
    Html.div [
        Facets.facetMenu model.FilterMenuOpen model.Facets model.SelectedFacets dispatch
        safeSearchNavBar
        Bulma.section [
            Bulma.container [
                Heading.title
                Heading.subtitle
                Search.createSearchPanel model dispatch
                Search.createSearchResults model dispatch
            ]
            model.SelectedProperty
            |> Option.map (modalView dispatch)
            |> Option.defaultValue Html.none
        ]
    ]