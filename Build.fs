open Azure.Data.Tables
open Fake.Core
open Fake.IO
open Farmer
open Farmer.Builders
open Farmer.Search
open Helpers
open System.Text.RegularExpressions

initializeContext()

let sharedPath = Path.getFullName "src/Shared"
let serverPath = Path.getFullName "src/Server"
let clientPath = Path.getFullName "src/Client"
let deployPath = Path.getFullName "deploy"

Target.create "Clean" (fun _ ->
    Shell.cleanDir deployPath
    run dotnet "fable clean --yes" clientPath
)

Target.create "InstallClient" (fun _ -> run npm "install" ".")

Target.create "Bundle" (fun _ ->
    [ "server", dotnet $"publish -c Release -o \"{deployPath}\"" serverPath
      "client", dotnet "fable -o output --run npm run build" clientPath ]
    |> runParallel
)

Target.create "Azure" (fun _ ->
    let searchName : string = "REPLACE WITH AZURE SEARCH NAME"
    let storageName : string = "REPLACE WITH STORAGE NAME"
    let appServiceName : string = "REPLACE WITH AZURE APP SERVICE NAME"
    let azCopyPath : string option = None //REPLACE WITH PATH TO AZCOPY.EXE e.g Some "C:\Program Files (x86)\Microsoft SDKs\Azure\AzCopy\AzCopy.exe"

    let azureSearch = search {
        name searchName
        sku Basic
    }

    let storage = storageAccount {
        name storageName
        add_private_container "properties"
        add_table "postcodes"
    }

    let web = webApp {
        name $"{appServiceName}"
        always_on
        sku WebApp.Sku.B1
        operating_system Linux
        runtime_stack (Runtime.DotNet "8.0")
        setting "storageName" storageName
        setting "storageConnectionString" storage.Key
        setting "searchName" searchName
        setting "searchKey" azureSearch.AdminKey
        zip_deploy "deploy"
    }

    let deployment = arm {
        location Location.WestEurope
        add_resource web
        add_resource azureSearch
        add_resource storage
        output "storageConnectionString" storage.Key
    }

    // Deploy above resources to Azure
    let outputs =
        deployment
        |> Deploy.execute "SAFE Search 3" Deploy.NoParameters

    let connectionString = outputs.["storageConnectionString"]

    let accountName, accountKey =
        let matcher = Regex.Match(connectionString, "AccountName=(?<AccountName>.*);.*AccountKey=(?<AccountKey>.*)(;|$)")
        matcher.Groups.["AccountName"].Value, matcher.Groups.["AccountKey"].Value

    let lookupNeedsPriming =
        let destinationTable =
            let tableClient = TableServiceClient connectionString
            tableClient.GetTableClient "postcodes"
        destinationTable.Query<TableEntity>(maxPerPage = 1) |> Seq.isEmpty

    if lookupNeedsPriming then
        let azCopyPath =
            azCopyPath
            |> Option.defaultWith(fun () -> failwith "No azcopy path found. Please see the readme file for ways to do this manually")

        printfn "No data found - now seeding postcode / geo-location lookup table with ~1.8m entries. This will take a few minutes."
        CreateProcess.fromRawCommandLine $"{azCopyPath}" $"copy https://compositionalit.blob.core.windows.net/postcodedata https://{storageName}.table.core.windows.net/postcodes --recursive"
        |> Proc.run
        |> ignore
    else
        printfn "Postcode lookup already exists, no seeding is required."
)

Target.create "Run" (fun _ ->
    run dotnet "build" sharedPath
    [ "server", dotnet "watch run" serverPath
      "client", dotnet "fable watch -o output --run npm run start" clientPath ]
    |> runParallel
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