module Index

open Elmish
open Fable.Remoting.Client
open Shared
open System

type AsyncOperation<'T, 'Q> = Start of 'T | Complete of 'Q
type Deferred<'T> = HasNotStarted | InProgress | Resolved of 'T

type SearchTextError = NoSearchText | InvalidPostcode

type SearchKind =
    | FreeTextSearch | LocationSearch
    member this.Value = match this with FreeTextSearch -> 1 | LocationSearch -> 2
    member this.Description = match this with FreeTextSearch -> "Free Text" | LocationSearch -> "Post Code"

type SearchState = Searching | CannotSearch of SearchTextError | CanSearch of string

type Model =
    {
        SearchText : string
        SelectedSearchKind : SearchKind
        Properties : Deferred<PropertyResult list>
        SelectedProperty : PropertyResult option
        HasLoadedSomeData : Boolean
    }
    member this.SearchTextError =
        if String.IsNullOrWhiteSpace this.SearchText then Some NoSearchText
        else
            match this.SelectedSearchKind with
            | LocationSearch ->
                if Validation.isValidPostcode this.SearchText then None
                else Some InvalidPostcode
            | FreeTextSearch ->
                None

    // member this.SearchState =
    //     if this.Properties = InProgress then Searching
    //     else
    //         match this.SearchTextError with
    //         | Some error -> CannotSearch error
    //         | None -> CanSearch this.SearchText
    member this.HasProperties =
        match this.Properties with
        | Resolved [] | InProgress | HasNotStarted -> false
        | Resolved _ -> true

type SearchMsg =
    | ByFreeText of AsyncOperation<string, PropertyResult list>
    | ByLocation of AsyncOperation<string, Result<PropertyResult list, string>>

type Msg =
    | SearchTextChanged of string
    | SearchKindSelected of SearchKind
    | DoPostcodeSearch of string
    | Search of SearchMsg
    | AppError of string
    | ViewProperty of PropertyResult
    | CloseProperty

let searchApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<ISearchApi>

let init () =
    let model = { SearchText = ""; SelectedSearchKind = FreeTextSearch; Properties = HasNotStarted; SelectedProperty = None; HasLoadedSomeData = false }
    model, Cmd.none

let update msg model =
    match msg with
    | SearchTextChanged value ->
        { model with SearchText = value }, Cmd.none
    | SearchKindSelected kind ->
        { model with
            SelectedSearchKind = kind
            Properties = HasNotStarted }, Cmd.none
    | Search (ByFreeText operation) ->
        match operation with
        | Start text ->
            { model with Properties = InProgress }, Cmd.OfAsync.either searchApi.FreeText { Text = text } (Complete >> ByFreeText >> Search) (string >> AppError)
        | Complete properties ->
            { model with Properties = Resolved properties; HasLoadedSomeData = true }, Cmd.none
    | Search (ByLocation operation) ->
        match operation with
        | Start postcode ->
            { model with Properties = InProgress }, Cmd.OfAsync.either searchApi.ByLocation { Postcode = postcode } (Complete >> ByLocation >> Search) (string >> AppError)
        | Complete (Ok properties) ->
            { model with Properties = Resolved properties; HasLoadedSomeData = true }, Cmd.none
        | Complete (Error message) ->
            model, Cmd.ofMsg (AppError message)
    | ViewProperty property ->
        { model with SelectedProperty = Some property }, Cmd.none
    | CloseProperty ->
        { model with SelectedProperty = None }, Cmd.none
    | DoPostcodeSearch postCode ->
        let commands = [
            SearchTextChanged postCode
            Search (ByLocation (Start postCode))
            SearchKindSelected LocationSearch
        ]
        model, Cmd.batch (List.map Cmd.ofMsg commands)
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
    let searchInput (model:Model) dispatch =
        Bulma.control.div [
            control.hasIconsLeft
            prop.children [
                Bulma.input.search [
                    prop.onChange (SearchTextChanged >> dispatch)
                    match model.SearchTextError, model.Properties with
                    | Some NoSearchText, _ ->
                        color.isPrimary
                        prop.placeholder "Enter your search term here."
                    | Some InvalidPostcode, _ ->
                        color.isDanger
                    | None, InProgress ->
                        prop.disabled true
                    | None, (HasNotStarted | Resolved _) ->
                        prop.valueOrDefault model.SearchText
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
            match model.SearchTextError, model.Properties with
            | Some _, _ ->
                prop.disabled true
            | None, InProgress ->
                button.isLoading
            | None, (HasNotStarted | Resolved _) ->
                match model.SelectedSearchKind with
                | FreeTextSearch -> prop.onClick(fun _ -> dispatch (Search (ByFreeText (Start model.SearchText))))
                | LocationSearch -> prop.onClick(fun _ -> dispatch (Search (ByLocation (Start model.SearchText))))
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
                column.is8
                prop.children [ searchInput model dispatch ]
            ]
            Bulma.column [
                column.is2
                prop.children [
                    Bulma.control.div [
                        control.hasIconsLeft
                        prop.children [
                            Bulma.select [
                                prop.disabled (model.Properties = InProgress)
                                prop.onChange (function
                                    | "1" -> dispatch (SearchKindSelected FreeTextSearch)
                                    | _ -> dispatch (SearchKindSelected LocationSearch)
                                )
                                prop.children [
                                    for kind in [ FreeTextSearch; LocationSearch ] do
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
                                            | LocationSearch -> "location-arrow"
                                        prop.className $"fas fa-{iconName}"
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
            Bulma.column [
                column.is2
                prop.children [ searchButton model dispatch ]
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
                        | LocationSearch ->
                            ColumnDef.valueGetter (fun x -> Option.toObj x.Address.PostCode)
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
                        resultsGrid dispatch model.SelectedSearchKind results
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