namespace Shared

open System

module Route =
    let builder typeName methodName =
        sprintf "/api/%s/%s" typeName methodName

type PropertyType =
    | Detached
    | SemiDetached
    | Terraced
    | FlatsMaisonettes
    | Other

    member this.Description =
        match this with
        | SemiDetached -> "Semi Detatch"
        | FlatsMaisonettes -> "Flats / Maisonettes"
        | _ -> string this

    static member Parse =
        function
        | "D" -> Some Detached
        | "S" -> Some SemiDetached
        | "T" -> Some Terraced
        | "F" -> Some FlatsMaisonettes
        | "O" -> Some Other
        | _ -> None

type BuildType =
    | NewBuild
    | OldBuild

    member this.Description =
        match this with
        | OldBuild -> "Old Build"
        | NewBuild -> "New Build"

    static member Parse =
        function
        | "Y" -> NewBuild
        | _ -> OldBuild

type ContractType =
    | Freehold
    | Leasehold
    member this.Description = string this
    static member Parse =
        function
        | "F" -> Freehold
        | _ -> Leasehold

type Geo =
    {
        Lat : float
        Long : float
    }

type Address =
    {
        Building : string
        Street : string option
        Locality : string option
        TownCity : string
        District : string
        County : string
        PostCode : string option
        GeoLocation : Geo option
    }
    member address.FirstLine =
        [ address.Building
          yield! Option.toList address.Street ]
        |> String.concat " "

type BuildDetails =
    {
        PropertyType : PropertyType option
        Build : BuildType
        Contract : ContractType
    }

type PropertyResult =
    {
        BuildDetails : BuildDetails
        Address : Address
        Price : int
        DateOfTransfer : DateTime
    }

type FreeTextSearchRequest = { Text : string }
type LocationSearchRequest = { Postcode : string }

type ISearchApi =
    {
        FreeText : FreeTextSearchRequest -> Async<PropertyResult list>
        ByLocation : LocationSearchRequest -> Async<Result<PropertyResult list, string>>
    }


/// Provides validation on data. Shared across both client and server.
module Validation =
    open System.Text.RegularExpressions

    let isValidPostcode postcode =
        Regex.IsMatch
            (postcode,
             @"([Gg][Ii][Rr] 0[Aa]{2})|((([A-Za-z][0-9]{1,2})|(([A-Za-z][A-Ha-hJ-Yj-y][0-9]{1,2})|(([A-Za-z][0-9][A-Za-z])|([A-Za-z][A-Ha-hJ-Yj-y][0-9]?[A-Za-z]))))\s?[0-9][A-Za-z]{2})")

[<AutoOpen>]
module Extensions =
    type Result<'T,'TError> with
        member this.IsError = match this with Error _ -> true | Ok _ -> false