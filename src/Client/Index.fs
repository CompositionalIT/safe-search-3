module Index

open Elmish
open Fable.Remoting.Client
open Shared
open System

type AsyncOperation<'T> = Start | Complete of 'T
type Deferred<'T> = HasNotStarted | InProgress | Resolved of 'T

type CannotSearchReason = NoSearchText | InvalidPostcode
type SearchKind = StandardSearch | LocationSearch
type SearchState = Searching | CannotSearch of CannotSearchReason | CanSearch

type Model =
    {
        SearchText : string
        SelectedSearchKind : SearchKind
        Properties : Deferred<PropertyResult list>
        SelectedProperty : PropertyResult option
        HasLoadedSomeData : Boolean
    }
    member this.SearchState =
        if String.IsNullOrWhiteSpace this.SearchText then CannotSearch NoSearchText
        elif this.Properties = InProgress then Searching
        else CanSearch
    member this.HasProperties =
        match this.Properties with
        | Resolved [] | InProgress | HasNotStarted -> false
        | Resolved _ -> true

type Msg =
    | SearchTextChanged of string
    | SearchKindSelected of SearchKind
    | FreeTextSearch of AsyncOperation<PropertyResult list>
    | LocationSearch of AsyncOperation<Result<PropertyResult list, string>>
    | AppError of exn
    | ViewProperty of PropertyResult
    | CloseProperty

let searchApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<ISearchApi>

let init () =
    let model = { SearchText = ""; SelectedSearchKind = StandardSearch; Properties = HasNotStarted; SelectedProperty = None; HasLoadedSomeData = false }
    model, Cmd.none

let update msg model =
    match msg with
    | SearchTextChanged value ->
        { model with SearchText = value }, Cmd.none
    | SearchKindSelected kind ->
        { model with SelectedSearchKind = kind }, Cmd.none
    | FreeTextSearch operation ->
        match operation with
        | Start ->
            { model with Properties = InProgress }, Cmd.OfAsync.either searchApi.FreeText { Text = Option.ofObj model.SearchText } (Complete >> FreeTextSearch) AppError
        | Complete properties ->
            { model with Properties = Resolved properties; HasLoadedSomeData = true }, Cmd.none
    | LocationSearch operation ->
        match operation with
        | Start ->
            { model with Properties = InProgress }, Cmd.OfAsync.either searchApi.ByLocation { Postcode = model.SearchText } (Complete >> LocationSearch) AppError
        | Complete (Ok properties) ->
            { model with Properties = Resolved properties; HasLoadedSomeData = true }, Cmd.none
        | Complete (Error message) ->
            Browser.Dom.console.log message
            model, Cmd.none
    | ViewProperty property ->
        { model with SelectedProperty = Some property }, Cmd.none
    | CloseProperty ->
        { model with SelectedProperty = None }, Cmd.none
    | AppError ex ->
        Browser.Dom.console.log ex
        model, Cmd.none

open Feliz
open Feliz.Bulma

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
            prop.children [
                Html.text "Find your unaffordable property in the UK!"
            ]
        ]

module Search =
    let searchInput model dispatch =
        Bulma.control.div [
            control.hasIconsLeft
            prop.value model.SearchText
            prop.onChange (SearchTextChanged >> dispatch)
            prop.children [
                Bulma.input.search [
                    match model.SearchState with
                    | CannotSearch NoSearchText ->
                        color.isPrimary
                        prop.placeholder "Enter your search term here."
                    | CannotSearch InvalidPostcode ->
                        color.isDanger
                    | Searching ->
                        prop.disabled true
                    | CanSearch ->
                        color.isPrimary
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
            ]
        ]
    let searchButton (model:Model) dispatch =
        Bulma.button.a [
            button.isFullWidth
            color.isPrimary
            match model.SearchState with
            | CannotSearch _ ->
                prop.disabled true
            | Searching ->
                button.isLoading
            | CanSearch ->
                match model.SelectedSearchKind with
                | SearchKind.StandardSearch -> prop.onClick(fun _ -> dispatch (FreeTextSearch Start))
                | SearchKind.LocationSearch -> prop.onClick(fun _ -> dispatch (LocationSearch Start))
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
                column.isThreeFifths
                prop.children [ searchInput model dispatch ]
            ]
            Bulma.column [
                column.isOneFifth
                prop.children [ searchButton model dispatch ]
            ]
            Bulma.column [
                column.isOneFifth
                prop.children [
                    Bulma.control.div [
                        control.hasIconsLeft
                        prop.children [
                            Bulma.select [
                                prop.onChange (function
                                    | "1" -> dispatch (SearchKindSelected StandardSearch)
                                    | _ -> dispatch (SearchKindSelected SearchKind.LocationSearch)
                                )
                                prop.children [
                                    Html.option [ prop.text "Standard Search"; prop.value "1" ]
                                    Html.option [ prop.text "Location Search"; prop.value "2" ]
                                ]
                            ]
                            Bulma.icon [
                                icon.isSmall
                                icon.isLeft
                                prop.children [
                                    Html.i [
                                        let iconName =
                                            match model.SelectedSearchKind with
                                            | StandardSearch -> "search"
                                            | SearchKind.LocationSearch -> "location-arrow"
                                        prop.className $"fas fa-{iconName}"
                                    ]
                                ]
                            ]
                        ]
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

open Feliz.AgGrid
open Fable.Core.JsInterop

let resultsGrid dispatch (results:PropertyResult list) =
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
                        ColumnDef.valueGetter (fun x -> x.Address.PostCode |> Option.toObj)
                    ]
                ]
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
                    | Resolved []
                    | HasNotStarted
                    | InProgress ->
                        ()
                    | Resolved results ->
                        resultsGrid dispatch results
                ]
                match model.SelectedProperty with
                | None ->
                    ()
                | Some property ->
                    Bulma.modal [
                        modal.isActive
                        prop.children [
                            Bulma.modalBackground [
                                prop.onClick (fun _ -> dispatch CloseProperty)
                            ]
                            Bulma.modalContent [
                                Bulma.box [
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

                                    makeLine "Street" [ $"{property.Address.Building}, {property.Address.Street |> Option.toObj}" ]
                                    makeLine "Town" [ property.Address.District; property.Address.County; (Option.toObj property.Address.PostCode) ]
                                    makeLine "Price" [ $"£{property.Price?toLocaleString()}" ]
                                    makeLine "Date" [ property.DateOfTransfer.ToShortDateString() ]
                                    makeLine "Build" [ property.BuildDetails.Build.Description; property.BuildDetails.Contract.Description; property.BuildDetails.PropertyType |> Option.map(fun p -> p.Description) |> Option.toObj ]
                                ]
                            ]
                            Bulma.modalClose [
                                modalClose.isLarge
                                prop.ariaLabel "close"
                                prop.onClick (fun _ -> dispatch CloseProperty)
                            ]
                        ]
                    ]
            ]
        ]
    ]