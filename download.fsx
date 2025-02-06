open System.IO
open System.Threading.Tasks
open System.Net.Http
open System
open System.Threading

let maxRequests = 10
Directory.SetCurrentDirectory __SOURCE_DIRECTORY__
let wait (t: Task<_>) = t |> Async.AwaitTask  |> Async.RunSynchronously

let downloadFile (client: HttpClient) (uri: Uri) = task {
    let filename = Path.GetFileName uri.LocalPath
    try
        if File.Exists filename then
            printfn $"♻️ Deleting existing file: {filename}"
            File.Delete filename

        printfn $"⏬ Downloading: %s{string uri} to {filename}"
        use! stream = client.GetStreamAsync uri
        use fs = new FileStream(filename, FileMode.CreateNew);
        do! stream.CopyToAsync fs
        printfn $"✅ Completed: {filename}"
        return Ok uri
    with ex ->
        printfn $"❌ Failure: {filename} - {ex.Message}"
        return Error uri
}

let readUris filename = task {
    let! files = File.ReadAllLinesAsync filename
    return files |> Array.map Uri
}

let downloadEach filename = task {
    use httpClient = new HttpClient()
    use semaphore = new SemaphoreSlim(maxRequests, maxRequests)

    let download uri = task {
        try
            do! semaphore.WaitAsync()
            let! result = downloadFile httpClient uri
            return result
        finally
            semaphore.Release() |> ignore
    }

    let! files = readUris filename
    let! results = files
                   |> Array.map download
                   |> Task.WhenAll

    let failures = results |> Array.choose (function Error uri -> Some uri | Ok _ -> None)
    printfn $"🪛 Retrying: {failures.Length} uris"
    do! failures
        |> Array.map download
        |> Task.WhenAll
        :> Task
}

downloadEach "urls.txt" |> wait
