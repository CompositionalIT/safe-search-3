[<AutoOpen>]
module Helpers

type AsyncOperation<'T, 'Q> =
    | Start of 'T
    | Complete of 'Q

type Deferred<'T> =
    | HasNotStarted
    | InProgress
    | Resolved of 'T

let (|IsLoading|IsNotLoading|) =
    function
    | HasNotStarted
    | Resolved _ -> IsNotLoading
    | InProgress -> IsLoading

let (|NonEmpty|_|) =
    function
    | (_ :: _) as items -> Some(NonEmpty items)
    | _ -> None