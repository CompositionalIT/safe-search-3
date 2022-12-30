open Fake.Core
open Fake.IO
open Farmer
open Farmer.Builders
open Farmer
open Farmer.Builders
open Farmer.Search

open Helpers
open Farmer.Storage
open System.Text.RegularExpressions
open Azure.Data.Tables

initializeContext()

let sharedPath = Path.getFullName "src/Shared"
let serverPath = Path.getFullName "src/Server"
let clientPath = Path.getFullName "src/Client"
let deployPath = Path.getFullName "deploy"

Target.create "Clean" (fun _ ->
    Shell.cleanDir deployPath
    run dotnet "fable clean --yes" clientPath // Delete *.fs.js files created by Fable
)

Target.create "InstallClient" (fun _ -> run npm "install" ".")

Target.create "Bundle" (fun _ ->
    [ "server", dotnet $"publish -c Release -o \"{deployPath}\" -r linux-x64" serverPath // may need to change
      "client", dotnet "fable --run webpack -p" clientPath ]
    |> runParallel
)

Target.create "Azure" (fun _ ->
    let searchName = "safesearch3-search"
    let storageName = "safesearch3storage"

    let azureSearch = search {
        name searchName
        sku Basic
    }

    let storage = storageAccount {
        name storageName
        add_private_container "properties"
        add_cors_rules [
            StorageService.Blobs, CorsRule.AllowAll
        ]
    }

    let web = webApp {
        name "safesearch3"
        zip_deploy "deploy"
        always_on
        sku WebApp.Sku.B1
        operating_system Linux
        automatic_logging_extension false
        runtime_stack Runtime.DotNet50
        setting "storageName" storageName
        setting "storageConnectionString" storage.Key
        setting "searchName" searchName
        setting "searchKey" azureSearch.AdminKey
    }

    let deployment = arm {
        location Location.WestEurope
        add_resource web
        add_resource azureSearch
        add_resource storage
        output "storageConnectionString" storage.Key
    }

    let result =
        deployment
        |> Deploy.execute "tom-safesearch3" Deploy.NoParameters

    let connectionString = result.["storageConnectionString"]
    let m = Regex.Match(connectionString, "AccountName=(?<AccountName>.*);.*AccountKey=(?<AccountKey>.*)(;|$)")
    let accountName = m.Groups.["AccountName"].Value
    let accountKey = m.Groups.["AccountKey"].Value
    let serviceClient = new TableServiceClient(connectionString)
    if serviceClient.Query "TableName eq 'postcodes2'" |> Seq.isEmpty then
        CreateProcess.fromRawCommandLine """AzCopy""" $"""/Source:https://compositionalit.blob.core.windows.net/postcodedata /Dest:https://{accountName}.table.core.windows.net/postcodes2 /DestKey:{accountKey} /Manifest:postcodes /EntityOperation:InsertOrReplace"""
        |> Proc.run
        |> ignore
)

Target.create "Run" (fun _ ->
    run dotnet "build" sharedPath
    [ "server", dotnet "watch run" serverPath
      "client", dotnet "fable watch --run webpack-dev-server" clientPath ]
    |> runParallel
)

Target.create "Format" (fun _ ->
    run dotnet "fantomas . -r" "src"
)

open Fake.Core.TargetOperators

let dependencies = [
    "Clean"
        ==> "InstallClient"
        ==> "Bundle"
        ==> "Azure"

    "Clean"
        ==> "InstallClient"
        ==> "Run"
]

[<EntryPoint>]
let main args = runOrDefault args