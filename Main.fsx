#r "nuget: MathNet.Numerics, 5.0.0"

#load "Data.fs"
#load "Portfolio.fs"
#load "Simulate.fs"
#load "Optimize.fs"

open System
open System.IO
open System.Diagnostics
open System.Text.Json

// Configuration
let dataFilePath = Path.Combine(__SOURCE_DIRECTORY__, "data", "dow_jones_close_prices_aug_dec_2024.csv")
let outputPath = Path.Combine(__SOURCE_DIRECTORY__, "results")
let selectionSize = 25 // Original selection size (25 out of 30 stocks)
let numPortfoliosPerCombination = 1000 // Original number of portfolios per combination
let batchSize = 100 // Batch size for vectorized operations
let progressReportInterval = 500 // Increased reporting interval for long runs

// Ensure output directory exists
Directory.CreateDirectory(outputPath) |> ignore

// Log command to chat logs
let logCommand cmd =
    let logDir = Path.Combine(__SOURCE_DIRECTORY__, "chat-logs")
    Directory.CreateDirectory(logDir) |> ignore
    
    let timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
    let logFilePath = Path.Combine(logDir, $"optimization_run_{timestamp}.log")
    
    File.AppendAllText(logFilePath, $"[{DateTime.Now}] {cmd}\n")

// Log the run
logCommand "Running complete portfolio optimization with all 142,506 combinations"

// Print header
printfn "=========================================================="
printfn "      Portfolio Optimization Simulator in F# (Vectorized)"
printfn "=========================================================="
printfn "Loading stock data from: %s" dataFilePath
printfn "Selection size: %d stocks" selectionSize
printfn "Portfolios per combination: %d" numPortfoliosPerCombination
printfn "Batch size for vectorization: %d" batchSize
printfn "Processing all possible combinations"
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
        0 // Process all combinations (0 means no limit)
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
    printfn "- Annual Return: %.2f%%" (portfolio.AnnualReturn * 100.0)
    printfn "- Annual Volatility: %.2f%%" (portfolio.AnnualVolatility * 100.0)
    
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
            AnnualReturn = portfolio.AnnualReturn
            AnnualVolatility = portfolio.AnnualVolatility
            ExecutionTime = stopwatch.Elapsed.ToString()
            Stocks = 
                portfolio.Stocks
                |> Array.mapi (fun i stock -> 
                    {| Ticker = stock.Ticker; Weight = portfolio.Weights.[i] |})
        |}
    
    let jsonOptions = JsonSerializerOptions()
    jsonOptions.WriteIndented <- true
    let json = JsonSerializer.Serialize(resultObj, jsonOptions)
    
    let outputFilePath = Path.Combine(outputPath, "optimal_portfolio.json")
    File.WriteAllText(outputFilePath, json)
    
    // Save results to a CSV file
    let csvOutput = 
        "Ticker,Weight\n" +
        (portfolio.Stocks
         |> Array.mapi (fun i stock -> sprintf "%s,%.6f" stock.Ticker portfolio.Weights.[i])
         |> String.concat "\n")
    
    let csvOutputFilePath = Path.Combine(outputPath, "optimal_portfolio.csv")
    File.WriteAllText(csvOutputFilePath, csvOutput)
    
    printfn "\nResults saved to:"
    printfn "- %s" outputFilePath
    printfn "- %s" csvOutputFilePath
    
| None ->
    printfn "No valid portfolio found!"

printfn "\nPress any key to exit..."
Console.ReadKey() |> ignore 