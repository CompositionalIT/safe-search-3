# SAFE Template

This template can be used to generate a full-stack web application using the [SAFE Stack](https://safe-stack.github.io/). It was created using the dotnet [SAFE Template](https://safe-stack.github.io/docs/template-overview/). If you want to learn more about the template why not start with the [quick start](https://safe-stack.github.io/docs/quickstart/) guide?

## Install pre-requisites

You'll need to install the following pre-requisites in order to build SAFE applications

* [.NET Core SDK](https://www.microsoft.com/net/download) 5.0 or higher
* [Node LTS](https://nodejs.org/en/download/)

## Azure Services pre-requisites

You'll need to following Azure resources provisioned

* An Azure Storage account with:
    * a container called properties.
    * a table called postcodes.
* An Azure Search instance with:
    * an index created via `Management.createIndex`.
    * a data source created via `Management.createBlobDataSource`.
    * an indexer created via `Management.createCsvIndexer`.
* Postcodes should be inserted into table storage before properties are imported
  * The fastest way to import these is to use [AzCopy 7.3](https://docs.microsoft.com/en-us/previous-versions/azure/storage/storage-use-azcopy#azcopy-with-table-support-v73) then run the following command:
    ```bash
    AzCopy.exe /Source:https://compositionalit.blob.core.windows.net/postcodedata /Dest:https://{YOUR_STORAGE_ACCOUNT}.table.core.windows.net/postcodes2 /DestKey:{YOUR_ACCESS_KEY} /Manifest:postcodes /EntityOperation:InsertOrReplace
    ```

## Starting the application

Before you run the project **for the first time only** you must install dotnet "local tools" with this command:

```bash
dotnet tool restore
```

You should also set the following config settings either as environment variables or in [user secrets](https://docs.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-6.0&tabs=windows#manage-user-secrets-with-visual-studio)

```json
{
    // Azure Search resource name
    "searchName": "my-azure-search",
    // Azure Search access key
    "searchKey": "MYSECRETKEY",
    // Azure Storage account connection string
    "storageConnectionString": "DefaultEndpointsProtocol=https;AccountName=mystorageaccount;AccountKey=MYSECRETKEY"
}
```

To concurrently run the server and the client components in watch mode use the following command:

```bash
dotnet run
```

Then open `http://localhost:8080` in your browser.

The build project in root directory contains a couple of different build targets. You can specify them after `--` (target name is case-insensitive).

Finally, there are `Bundle` and `Azure` targets that you can use to package your app and deploy to Azure, respectively:

```bash
dotnet run -- Bundle
dotnet run -- Azure
```

## SAFE Stack Documentation

If you want to know more about the full Azure Stack and all of it's components (including Azure) visit the official [SAFE documentation](https://safe-stack.github.io/docs/).

You will find more documentation about the used F# components at the following places:

* [Saturn](https://saturnframework.org/)
* [Fable](https://fable.io/docs/)
* [Elmish](https://elmish.github.io/elmish/)
