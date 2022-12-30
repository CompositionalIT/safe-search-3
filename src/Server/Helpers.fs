module Helpers

open Microsoft.Extensions.Configuration

type IConfiguration with
    member this.StorageName = this.["storageName"]
    member this.SearchIndexName = this.["searchName"]
    member this.SearchIndexKey = this.["searchKey"]
    member this.StorageConnectionString = this.["storageConnectionString"]