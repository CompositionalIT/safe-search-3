module Helpers

open Microsoft.Extensions.Configuration

type IConfiguration with
    member this.StorageName = this.["storageName"]
    member this.SearchName = this.["searchName"]
    member this.SearchKey = this.["searchKey"]
    member this.StorageConnectionString = this.["storageConnectionString"]