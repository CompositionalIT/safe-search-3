namespace Kibalta

/// Specifies the sort direction.
type Direction =
    | Ascending
    | Descending
    member this.StringValue =
        match this with
        | Ascending -> "asc"
        | Descending -> "desc"

/// Specifies a type of sort to apply.
type SortColumn =
    | ByField of field: string * Direction
    | ByDistance of field: string * long: float * lat: float * Direction
    member this.StringValue =
        match this with
        | ByField(field, dir) -> sprintf "%s %s" field dir.StringValue
        | ByDistance(field, long, lat, dir) ->
            sprintf "geo.distance(%s, geography'POINT(%f %f)') %s" field long lat dir.StringValue

module Filters =
    /// Combines two filters together using either AND or OR logic.
    type FilterCombiner =
        | And
        | Or

    /// The types of filter comparisons.
    type FilterComparison =
        | Eq
        | Ne
        | Gt
        | Lt
        | Ge
        | Le
        member this.StringValue =
            match this with
            | Eq -> "eq"
            | Ne -> "ne"
            | Gt -> "gt"
            | Lt -> "lt"
            | Ge -> "ge"
            | Le -> "le"

    type FilterExpr =
        | ConstantFilter of bool
        | FieldFilter of field: string * FilterComparison * value: obj
        | GeoDistanceFilter of field: string * long: float * lat: float * FilterComparison * distance: float
        | BinaryFilter of FilterExpr * FilterCombiner * FilterExpr

        /// ANDs two filters together
        static member (+) (a, b) = BinaryFilter(a, And, b)

        /// ORs two filters together
        static member (*) (a, b) = BinaryFilter(a, Or, b)

    let DefaultFilter = ConstantFilter true
    // A helper to create a basic field filter.
    let where a comp b = FieldFilter(a, comp, b)
    // A helper to create a basic geo filter.
    let whereGeoDistance field (long, lat) comp b = GeoDistanceFilter(field, long, lat, comp, b)

    /// Combines two filters by ANDing them together.
    let combine = List.fold (+) DefaultFilter

    /// Creates a simple equality check filter.
    let whereEq (a, b) = where a Eq b

    let rec eval =
        function
        | ConstantFilter value -> sprintf "%b" value
        | FieldFilter(field, comparison, value) ->
            match value with
            | :? string as s -> sprintf "%s %s '%s'" field comparison.StringValue s
            | null -> sprintf "%s %s null" field comparison.StringValue
            | s -> sprintf "%s %s %O" field comparison.StringValue s
        | GeoDistanceFilter(field, long, lat, comparison, distance) ->
            let lhs = sprintf "geo.distance(%s, geography'POINT(%f %f)')" field long lat
            sprintf "%s %s %f" lhs comparison.StringValue distance
        | BinaryFilter(left, And, right) -> sprintf "%s and %s" (eval left) (eval right)
        | BinaryFilter(left, Or, right) -> sprintf "%s or %s" (eval left) (eval right)