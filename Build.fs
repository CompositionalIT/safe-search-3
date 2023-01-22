open Azure.Data.Tables
open Fake.Core
open Fake.IO
open Farmer
open Farmer.Builders
open Farmer.Search
open Helpers
open System.Text.RegularExpressions

initializeContext ()

let sharedPath = Path.getFullName "src/Shared"
let serverPath = Path.getFullName "src/Server"
let clientPath = Path.getFullName "src/Client"
let deployPath = Path.getFullName "deploy"

let tryPrimePostcodeLookup connectionString =
    let azCopyPath = @"C:\Program Files (x86)\Microsoft SDKs\Azure\AzCopy\AzCopy.exe"

    let accountName, accountKey =
        let matcher =
            Regex.Match(connectionString, "AccountName=(?<AccountName>.*);.*AccountKey=(?<AccountKey>.*)(;|$)")

        matcher.Groups.["AccountName"].Value, matcher.Groups.["AccountKey"].Value

    let lookupNeedsPriming =
        let destinationTable =
            let tableClient = TableServiceClient connectionString
            tableClient.GetTableClient "postcodes"

        destinationTable.Query<TableEntity>(maxPerPage = 1) |> Seq.isEmpty

    if lookupNeedsPriming then
        printfn
            "No data found - now seeding postcode / geo-location lookup table with ~1.8m entries. This will take a few minutes."

        CreateProcess.fromRawCommandLine
            $"{azCopyPath}"
            $"/Source:https://compositionalit.blob.core.windows.net/postcodedata /Dest:https://{accountName}.table.core.windows.net/postcodes /DestKey:{accountKey} /Manifest:postcodes /EntityOperation:InsertOrReplace"
        |> Proc.run
        |> ignore
    else
        printfn "Postcode lookup already exists, no seeding is required."

let createAzureResources (storageName: string) (searchName: string) (appServiceName: string) =
    let azureSearch = search {
        name searchName
        sku Basic
    }

    let storage = storageAccount {
        name storageName
        add_private_container "properties"
        add_table "postcodes"
    }

    let logs = logAnalytics { name "isaac-analytics" }

    let insights = appInsights {
        name "isaac-insights"
        log_analytics_workspace logs
    }

    let web = webApp {
        name appServiceName
        always_on
        link_to_app_insights insights
        sku WebApp.Sku.B1
        operating_system Linux
        runtime_stack Runtime.DotNet60
        setting "storageName" storageName
        setting "searchName" searchName
        setting "storageConnectionString" storage.Key
        setting "searchKey" azureSearch.AdminKey
        zip_deploy "deploy"
    }

    let deployment = arm {
        location Location.WestEurope
        add_resources [ web; azureSearch; storage; logs; insights ]
        output "storageConnectionString" storage.Key
        output "searchKey" azureSearch.AdminKey
    }

    deployment |> Deploy.execute "isaac-safe-search-3" Deploy.NoParameters

let createDevSettings storageName storageConnectionString searchName searchKey =
    printfn "Setting user secrets for server with storage and search keys for local development"

    let setSecret key value =
        dotnet $"user-secrets set \"{key}\" \"{value}\"" serverPath
        |> Proc.run
        |> ignore

    setSecret "storageName" storageName
    setSecret "storageConnectionString" storageConnectionString
    setSecret "searchName" searchName
    setSecret "searchKey" searchKey

Target.create "Azure" (fun _ ->
    let searchName = "isaac-safesearch-index"
    let storageName = "isaacsafestorage"
    let appServiceName = "isaac-safesearch-web"

    let storageConnectionString, searchKey =
        let outputs = createAzureResources storageName searchName appServiceName
        outputs.["storageConnectionString"], outputs.["searchKey"]

    tryPrimePostcodeLookup storageConnectionString
    createDevSettings storageName storageConnectionString searchName searchKey)

Target.create "Bundle" (fun _ ->
    run npm "install --prefer-offline --no-audit --progress=false" "."

    [
        "server", dotnet $"publish -c Release -o \"{deployPath}\"" serverPath
        "client", dotnet "fable --run vite build" clientPath
    ]
    |> runParallel)

Target.create "Run" (fun _ ->
    run dotnet "build" sharedPath

    [
        "server", dotnet "watch run" serverPath
        "client", dotnet "fable watch -s --run vite" clientPath
    ]
    |> runParallel)

open Fake.Core.TargetOperators

let dependencies = [ "Bundle" ==> "Azure" ]

[<EntryPoint>]
let main args = runOrDefault args