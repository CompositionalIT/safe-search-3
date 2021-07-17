module Index

open Elmish
open Fable.Remoting.Client
open Shared
open System

type CannotSearchReason = NoSearchText | InvalidPostcode
type SearchKind = StandardSearch | LocationSearch
type SearchState = Searching | CannotSearch of CannotSearchReason | CanSearch
type Model =
    {
        SearchText : string
        SelectedSearchKind : SearchKind
    }
    member this.SearchState =
        if String.IsNullOrWhiteSpace this.SearchText then CannotSearch NoSearchText
        else CanSearch

type Msg =
    | SearchTextChanged of string
    | StartSearch
    | SearchKindSelected of SearchKind

let todosApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<ITodosApi>

let init () =
    let model = { SearchText = ""; SelectedSearchKind = StandardSearch }
    model, Cmd.none

let update msg model =
    match msg with
    | SearchTextChanged value -> { model with SearchText = value }, Cmd.none
    | StartSearch -> model, Cmd.none
    | SearchKindSelected kind -> { model with SelectedSearchKind = kind }, Cmd.none

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
                prop.onClick(fun _ -> dispatch StartSearch)
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
                                    | _ -> dispatch (SearchKindSelected LocationSearch)
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
                                            | LocationSearch -> "location-arrow"
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

let view model dispatch =
    Html.div [
        safeSearchNavBar
        Bulma.section [
            section.isLarge
            prop.children [
                Bulma.container [
                    Heading.title
                    Heading.subtitle
                    Search.createSearchPanel model dispatch
                ]
            ]
        ]
    ]