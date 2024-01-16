## Install pre-requisites

You'll need to install the following pre-requisites in order to build SAFE applications

* [.NET Core SDK](https://www.microsoft.com/net/download) 5.0 or higher
* [Node LTS](https://nodejs.org/en/download/)

## Azure Services pre-requisites

You'll need to following Azure resources provisioned (these get created automatically by runing `dotnet run Azure`):

* An Azure Storage account with:
    * a container called **properties**.
    * a table called **postcodes**.
* An Azure Search instance with:
    * an index created via `Management.createIndex`.
    * a data source created via `Management.createBlobDataSource`.
    * an indexer created via `Management.createCsvIndexer`.
* Postcodes should be inserted into table storage before properties are imported
  * The fastest way to import these is to use [AzCopy 7.3](https://docs.microsoft.com/en-us/previous-versions/azure/storage/storage-use-azcopy#azcopy-with-table-support-v73) - (Windows only) then run the following command:
    ```bash
    AzCopy.exe /Source:https://compositionalit.blob.core.windows.net/postcodedata /Dest:https://{YOUR_STORAGE_ACCOUNT}.table.core.windows.net/postcodes2 /DestKey:{YOUR_ACCESS_KEY} /Manifest:postcodes /EntityOperation:InsertOrReplace
    ```

  * Alternatively you can use [Azure Storage Explorer](https://azure.microsoft.com/en-gb/products/storage/storage-explorer) - (Windows, Mac, Linux) and do the following steps.

    - Connect to https://compositionalit.blob.core.windows.net/postcodedata
    - Download the postcodes.csv file
    - Connect to your newly created table storage account and the table called **postcodes**
    - Import the postcode.csv file

# Getting started

Before you run the project **for the first time only** you must install dotnet "local tools" with this command:

```bash
dotnet tool restore
```

## Running in Azure (preferred)
**Set the names for web app, Azure Search, and storage instances, as well as the path to [AzCopy](https://docs.microsoft.com/en-us/previous-versions/azure/storage/storage-use-azcopy#azcopy-with-table-support-v73) in Build.fs:31-34.**
Navigate to the folder containing the code (e.g. C:\\safe-search-3\\) and run the following command to deploy the app to Azure:
```bash
dotnet run Azure
```

The first time you do the deployment it will automatically populate the postcodes and properties (this may take a while).

## Running locally
Requirements:
* **This requires an Azure Search instace deployed to Azure**
* [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite?tabs=visual-studio) set up locally

You should also set the following config settings either as environment variables or in [user secrets](https://docs.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-6.0&tabs=windows#manage-user-secrets-with-visual-studio)

```json
{
    // Azure Search resource name
    "searchName": "my-azure-search",
    // Azure Search access key (Search service > Keys > Primary admin key)
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
