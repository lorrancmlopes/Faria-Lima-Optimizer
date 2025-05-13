#r "nuget: MathNet.Numerics, 5.0.0"

#load "Data.fs"
#load "Portfolio.fs"
#load "Simulate.fs"
#load "Optimize.fs"

open System
open System.IO
open System.Diagnostics
open System.Text.Json

let args = Environment.GetCommandLineArgs()

let mutable useParallelism = true
for arg in args do
    match arg.ToLower() with
    | "--sequential" | "-s" -> useParallelism <- false
    | _ -> ()

// Configuration
let dataFilePath = Path.Combine(__SOURCE_DIRECTORY__, "data", "dow_jones_close_prices_aug_dec_2024.csv")
let outputPath = Path.Combine(__SOURCE_DIRECTORY__, "results")
let selectionSize = 25 // Reduced selection size for faster runs
let numPortfoliosPerCombination = 1000 
let batchSize = 100 // Batch size for vectorized operations
let progressReportInterval = 500 
let maxCombinationsToProcess = 0  // 0 means process ALL combinations

// Ensure output directory exists
Directory.CreateDirectory(outputPath) |> ignore

let logCommand cmd =
    let logDir = Path.Combine(__SOURCE_DIRECTORY__, "logs")
    Directory.CreateDirectory(logDir) |> ignore
    
    let timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
    let logFilePath = Path.Combine(logDir, $"optimization_run_{timestamp}.log")
    
    File.AppendAllText(logFilePath, $"[{DateTime.Now}] {cmd}\n")


// Log benchmark information for later comparison
let logBenchmark (executionMode: string) (parameters: string) (executionTime: TimeSpan) (result: string) =
    let logDir = Path.Combine(__SOURCE_DIRECTORY__, "results")
    Directory.CreateDirectory(logDir) |> ignore
    
    // Always append to benchmark log files instead of overwriting
    let logFilePath = Path.Combine(logDir, "benchmarks.log")
    let timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
    let processorCount = Environment.ProcessorCount
    
    // Format: timestamp,mode,selectionSize,portfoliosPerCombo,maxCombos,batchSize,totalMilliseconds,result,cpuCores
    let logEntry = sprintf "%s,%s,%d,%d,%d,%d,%.2f,%s,%d\n" 
                       timestamp executionMode selectionSize numPortfoliosPerCombination 
                       maxCombinationsToProcess batchSize executionTime.TotalMilliseconds 
                       result processorCount
    
    if not (File.Exists(logFilePath)) then
        File.WriteAllText(logFilePath, "Timestamp,ExecutionMode,SelectionSize,PortfoliosPerCombo,MaxCombinations,BatchSize,TimeMs,BestSharpeRatio,CPUCores\n")
    
    File.AppendAllText(logFilePath, logEntry)
    
    let readableLogPath = Path.Combine(logDir, "benchmark_results.txt")
    let readableEntry = sprintf "[%s] %s mode: %d stocks, %d portfolios/combo, %d max combos, %d batch size => %s (%.2f sec) on %d cores\n" 
                            timestamp executionMode selectionSize numPortfoliosPerCombination 
                            maxCombinationsToProcess batchSize result executionTime.TotalSeconds processorCount
    
    File.AppendAllText(readableLogPath, readableEntry)
    
    let timestampForFile = DateTime.Now.ToString("yyyyMMdd_HHmmss")
    logFilePath

let executionMode = if useParallelism then "parallel" else "sequential"
logCommand $"Running portfolio optimization in {executionMode} mode with {selectionSize} stocks, {numPortfoliosPerCombination} portfolios per combination, limited to {maxCombinationsToProcess} combinations"

printfn "=========================================================="
printfn "      Portfolio Optimization Simulator in F# (Vectorized)"
printfn "=========================================================="
printfn "Execution mode: %s" (if useParallelism then "PARALLEL" else "SEQUENTIAL")
printfn "Loading stock data from: %s" dataFilePath
printfn "Selection size: %d stocks" selectionSize
printfn "Portfolios per combination: %d" numPortfoliosPerCombination
printfn "Batch size for vectorization: %d" batchSize
printfn "Max combinations: %d" maxCombinationsToProcess
printfn "=========================================================="

// Main execution
let stopwatch = Stopwatch.StartNew()

// Load stock data
let stocks = Data.loadStockData dataFilePath

// Run the optimization with vectorized operations
let bestPortfolio = 
    Optimize.findOptimalPortfolioVectorized 
        stocks 
        selectionSize 
        numPortfoliosPerCombination 
        batchSize
        progressReportInterval
        maxCombinationsToProcess
        useParallelism
        Optimize.defaultProgressHandler

// Print results
stopwatch.Stop()
printfn "\n=========================================================="
printfn "Optimization completed in %s" (stopwatch.Elapsed.ToString(@"hh\:mm\:ss\.fff"))

match bestPortfolio with
| Some portfolio ->
    let tickers = portfolio.Stocks |> Array.map (fun s -> s.Ticker)
    
    printfn "\nBest Portfolio:"
    printfn "- Sharpe Ratio: %.6f" portfolio.SharpeRatio
    
    printfn "\nSelected Stocks and Weights:"
    portfolio.Stocks
    |> Array.zip portfolio.Weights
    |> Array.sortByDescending fst
    |> Array.iter (fun (weight, stock) ->
        printfn "- %s: %.2f%%" stock.Ticker (weight * 100.0))
    
    // Save results to a JSON file
    let resultObj = 
        {| 
            SharpeRatio = portfolio.SharpeRatio
            ExecutionTime = stopwatch.Elapsed.ToString()
            ExecutionMode = executionMode
            SelectionSize = selectionSize
            PortfoliosPerCombination = numPortfoliosPerCombination
            MaxCombinations = maxCombinationsToProcess
            BatchSize = batchSize
            TimeElapsedMs = stopwatch.ElapsedMilliseconds
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            Stocks = 
                portfolio.Stocks
                |> Array.mapi (fun i stock -> 
                    {| Ticker = stock.Ticker; Weight = portfolio.Weights.[i] |})
        |}
    
    let jsonOptions = JsonSerializerOptions()
    jsonOptions.WriteIndented <- true
    let json = JsonSerializer.Serialize(resultObj, jsonOptions)
    
    let timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss")
    let outputFilePath = Path.Combine(outputPath, $"optimal_portfolio_{executionMode}_{timestamp}.json")
    File.WriteAllText(outputFilePath, json)
    
    // Save results to a CSV file
    let csvOutput = 
        "Ticker,Weight\n" +
        (portfolio.Stocks
         |> Array.mapi (fun i stock -> sprintf "%s,%.6f" stock.Ticker portfolio.Weights.[i])
         |> String.concat "\n")
    
    let csvOutputFilePath = Path.Combine(outputPath, $"optimal_portfolio_{executionMode}_{timestamp}.csv")
    File.WriteAllText(csvOutputFilePath, csvOutput)
    
    // Log benchmark results
    let resultSummary = sprintf "Sharpe=%.6f" portfolio.SharpeRatio
    let benchmarkLogPath = logBenchmark executionMode 
                               $"s{selectionSize}_p{numPortfoliosPerCombination}_m{maxCombinationsToProcess}_b{batchSize}" 
                               stopwatch.Elapsed 
                               resultSummary
    
    printfn "\nResults saved to:"
    printfn "- %s" outputFilePath
    printfn "- %s" csvOutputFilePath
    printfn "- Benchmark logged to: %s" benchmarkLogPath
    
| None ->
    printfn "No valid portfolio found!"
    // Log benchmark results for failed run
    let resultSummary = "NoValidPortfolio"
    let benchmarkLogPath = logBenchmark executionMode 
                               $"s{selectionSize}_p{numPortfoliosPerCombination}_m{maxCombinationsToProcess}_b{batchSize}" 
                               stopwatch.Elapsed 
                               resultSummary
    () 

printfn "\nExecuted in %s mode. Total time: %s" executionMode (stopwatch.Elapsed.ToString(@"hh\:mm\:ss\.fff"))
printfn "\nTo compare execution modes, run with:"
printfn "dotnet fsi Main.fsx             # Parallel mode (default)"
printfn "dotnet fsi Main.fsx --sequential  # Sequential mode"
printfn "\nBenchmark information saved for later comparison."
printfn "\nPress any key to exit..."
Console.ReadKey() |> ignore 