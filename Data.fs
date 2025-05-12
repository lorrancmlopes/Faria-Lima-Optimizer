module Data

open System
open System.IO

/// Stock data structure
type Stock = {
    Ticker: string
    Prices: float array
    Returns: float array
}

/// Load stock data from a CSV file
let loadStockData (filePath: string) =
    if not (File.Exists(filePath)) then
        failwith $"Data file not found: {filePath}"
    
    // Read all lines from the CSV file
    let lines = File.ReadAllLines(filePath)
    if lines.Length < 2 then
        failwith "CSV file does not contain enough data"
    
    // Parse header to get dates
    let header = lines.[0].Split(',')
    let dateColumns = header.[1..] // Skip the ticker column
    
    // Create stocks from each row (each row is a ticker)
    let stocks = 
        lines
        |> Array.skip 1 // Skip header row
        |> Array.map (fun line ->
            let columns = line.Split(',')
            if columns.Length < 2 then
                failwith $"Invalid data format for line: {line}"
                
            let ticker = columns.[0]
            let prices = 
                columns.[1..]
                |> Array.map (fun priceStr ->
                    match Double.TryParse(priceStr) with
                    | true, price -> price
                    | false, _ -> failwith $"Invalid price data for ticker {ticker}")
                
            // Calculate daily returns: (price_t - price_{t-1}) / price_{t-1}
            // No capping applied - allowing all return values as-is
            let returns = 
                prices
                |> Array.pairwise
                |> Array.map (fun (prevPrice, currPrice) -> 
                    if prevPrice <= 0.0 then 0.0 // Prevent division by zero
                    else (currPrice - prevPrice) / prevPrice)
            
            { Ticker = ticker; Prices = prices; Returns = returns })
    
    // Print statistics to help diagnose issues - commented out to reduce output
    (*
    let allReturns = stocks |> Array.collect (fun s -> s.Returns)
    if allReturns.Length > 0 then
        let minReturn = Array.min allReturns
        let maxReturn = Array.max allReturns
        let avgReturn = Array.average allReturns
        printfn "Return statistics - Min: %.6f, Max: %.6f, Avg: %.6f" minReturn maxReturn avgReturn
    *)
    
    printfn "Loaded %d tickers with price data for %d days" stocks.Length (if stocks.Length > 0 then stocks.[0].Prices.Length else 0)
    stocks

/// Creates a matrix of returns for a set of stocks
/// Each row represents the returns for one stock
let createReturnsMatrix (stocks: Stock array) =
    stocks |> Array.map (fun stock -> stock.Returns) 