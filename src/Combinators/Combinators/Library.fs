namespace ConCombinators
    

  
module AsyncDemo =
  open System
  open System.IO
  open System.Net
  open System.Threading

  // ------------------------------------------------------------------
  // Read a stream into the memory and then return it as a string
  // ------------------------------------------------------------------

  let readToEnd (stream:Stream) = async {
    // Allocate 1kb buffer for downloading dat
    let buffer = Array.zeroCreate 1024
    use output = new MemoryStream()
    let finished = ref false
    
    while not finished.Value do
      // Download one (at most) 1kb chunk and copy it
      let! count = stream.AsyncRead(buffer, 0, 1024)
      do! output.AsyncWrite(buffer, 0, count)
      finished := count <= 0

    // Read all data into a string
    output.Seek(0L, SeekOrigin.Begin) |> ignore
    use sr = new StreamReader(output)
    return sr.ReadToEnd() }


  // ------------------------------------------------------------------
  // Download a web page using HttpWebRequest
  // ------------------------------------------------------------------

  /// Downlaod content of a web site using 'readToEnd'
  let download url = async {
    let request = HttpWebRequest.Create(Uri(url))
    use! response = request.AsyncGetResponse()
    use stream = response.GetResponseStream()
    let! res = readToEnd stream 
    return res }

  // Create a list of sites to download 

  let sites = 
    [ "http://webstep.no"
      "http://microsoft.com"
      "http://bing.com"
      "http://google.com" ]

  // Fork-join parallelism (download all)
  let downloads = sites |> List.map download
  let alldata = Async.Parallel downloads

  // Run synchronously & block the thread
  let res = Async.RunSynchronously(alldata)

  // Create a background task that will eventually
  // print (or do some other side-effect)
  let work = async { 
    let! res = alldata 
    for it in res do
      printfn "%d" it.Length }

  Async.Start(work)
    
  
module AsyncAndReactice =
  open System


