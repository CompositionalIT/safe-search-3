open Fake.Core
open Fake.IO
open Farmer
open Farmer.Builders

open Helpers

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
    let searchKey = Environment.environVarOrFail "searchKey"
    let searchName = Environment.environVarOrFail "searchName"
    let storageConnectionString = Environment.environVarOrFail "storageConnectionString"

    let web = webApp {
        name "safesearch3"
        zip_deploy "deploy"
        always_on
        sku WebApp.Sku.B1
        operating_system Linux
        automatic_logging_extension false
        runtime_stack Runtime.DotNet50
        setting "searchKey" searchKey
        setting "searchName" searchName
        setting "storageConnectionString" storageConnectionString

    }

    let deployment = arm {
        location Location.WestEurope
        add_resource web
    }

    deployment
    |> Deploy.execute "safesearch3" Deploy.NoParameters
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