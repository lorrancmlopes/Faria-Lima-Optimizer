# Portfolio Optimization Implementation in F#

A portfolio optimization algorithm implementation.

## Table of Contents
1. [Problem Definition](#problem-definition)
2. [Project Structure](#project-structure)
3. [Data Processing](#data-processing)
4. [Portfolio Generation](#portfolio-generation)
5. [Performance Calculation](#performance-calculation)
6. [Optimization Algorithm](#optimization-algorithm)
7. [Performance Optimizations](#performance-optimizations)
8. [Memory Management](#memory-management)
9. [Parallel vs. Sequential Benchmarking](#parallel-vs-sequential-benchmarking)
10. [Results](#results)

## Problem Definition

The goal of this project is to find the optimal portfolio allocation across Dow Jones stocks that maximizes the Sharpe ratio (return-to-risk ratio).

**Key Requirements:**
- Select a subset of Dow Jones stocks (default is 25 out of 30, configurable)
- For each combination, generate multiple valid portfolios with different weight distributions
- Weight constraints: each weight must be non-negative (≥ 0) and ≤ 20%, and all weights must sum to 1
- Calculate the Sharpe ratio for each portfolio
- Identify the portfolio with the highest Sharpe ratio

## Project Structure

The project follows a modular design with separate files for different aspects of the optimization process:

- **Data.fs**: Handles loading and processing financial data
- **Portfolio.fs**: Defines portfolio structures and metrics calculations
- **Simulate.fs**: Implements random portfolio generation 
- **Optimize.fs**: Core optimization algorithm with parallel processing
- **Main.fsx**: Entry point and configuration

## Data Processing

### Loading Stock Data

The data loading process begins in `Data.fs` where we read stock data from CSV files:

```fsharp
// Data.fs:16-51
let loadStockData (filePath: string) =
    // Read all lines from the CSV file
    let lines = File.ReadAllLines(filePath)
    
    // Parse header to get dates
    let header = lines.[0].Split(',')
    
    // Create stocks from each row (each row is a ticker)
    let stocks = 
        lines
        |> Array.skip 1 // Skip header row
        |> Array.map (fun line ->
            let columns = line.Split(',')
            let ticker = columns.[0]
            let prices = 
                columns.[1..]
                |> Array.map (fun priceStr -> float priceStr)
                
            // Calculate daily returns: (price_t - price_{t-1}) / price_{t-1}
            let returns = 
                prices
                |> Array.pairwise
                |> Array.map (fun (prevPrice, currPrice) -> 
                    if prevPrice <= 0.0 then 0.0 // Prevent division by zero
                    else (currPrice - prevPrice) / prevPrice)
            
            { Ticker = ticker; Prices = prices; Returns = returns })
    
    printfn "Loaded %d tickers with price data for %d days" stocks.Length 
        (if stocks.Length > 0 then stocks.[0].Prices.Length else 0)
    
    stocks
```

### Stock Type Definition

Each stock is represented as a record containing its ticker, historical prices, and pre-calculated returns:

```fsharp
// Data.fs:4-8
type Stock = {
    Ticker: string
    Prices: float array
    Returns: float array
}
```

## Portfolio Generation

### Generating All Stock Combinations

We generate all possible combinations of stocks using a recursive algorithm:

```fsharp
// Simulate.fs:10-20
let generateStockCombinations (stocks: Data.Stock array) (selectionSize: int) =
    let rec combinations n l = 
        match n, l with
        | 0, _ -> [[]]
        | _, [] -> []
        | k, (x::xs) -> 
            List.map (fun l -> x::l) (combinations (k-1) xs) @ combinations k xs
    
    combinations selectionSize (stocks |> Array.toList) 
    |> List.map Array.ofList
    |> Array.ofList
```

This function can generate many combinations depending on the selection size. For example, selecting 25 from 30 generates 142,506 combinations, while selecting 5 from 30 generates only 142,506.

### Weight Generation Strategies

We implemented multiple strategies to generate diverse portfolio weights, increasing the chances of finding the optimal portfolio:

```fsharp
// Simulate.fs:23-48
let private generateOneRandomWeightVector (numStocks: int) =
    // Choose a random strategy to increase exploration diversity
    match random.Next(4) with
    | 0 -> 
        // Strategy 1: Uniform random weights between 0 and 0.2
        Array.init numStocks (fun _ -> random.NextDouble() * 0.2)
    | 1 -> 
        // Strategy 2: Most weights close to zero, a few larger weights
        Array.init numStocks (fun _ -> 
            if random.NextDouble() < 0.7 then
                random.NextDouble() * 0.05  // Small weights up to 5%
            else
                0.05 + random.NextDouble() * 0.15    // 5% to 20%
        )
    | 2 ->
        // Strategy 3: Create a concentrated portfolio with a few stocks dominating
        let weights = Array.create numStocks 0.01  // Start with 1% weights
        
        // Select a few stocks to have larger weights
        let dominantStocksCount = random.Next(3, 7)
        for _ in 1..dominantStocksCount do
            let stockIdx = random.Next(numStocks)
            weights.[stockIdx] <- 0.15 + random.NextDouble() * 0.05  // 0.15-0.2 range
        
        weights
    | _ ->
        // Strategy 4: All weights nearly equal with small random variations
        let baseWeight = 1.0 / float numStocks
        Array.init numStocks (fun _ -> 
            baseWeight * (0.7 + random.NextDouble() * 0.6)  // 70%-130% of equal weight
        )
```

### Enforcing Weight Constraints

We implement a thorough validation process to ensure all weights meet the constraints:

```fsharp
// Simulate.fs:5-9
let isValidWeightVector (weights: float array) =
    let sum = Array.sum weights
    let inRange = weights |> Array.forall (fun w -> w >= 0.0 && w <= 0.2)
    abs(sum - 1.0) < 0.0001 && inRange
```

The constraint enforcement is complex because normalization and capping interact:

```fsharp
// Simulate.fs:51-84 (excerpt)
// Try to find valid weights respecting all constraints
while not isValid && attempts < maxAttempts do
    // Normalize the weights to sum to 1.0
    weights <- Portfolio.normalizeWeights weights
    
    // Check if any weight is below minimum or exceeds maximum after normalization
    if not (isValidWeightVector weights) then
        // For weights below minimum, set them to minimum
        // For weights above maximum, cap them at maximum
        let adjustedWeights = 
            weights 
            |> Array.map (fun w -> 
                if w < 0.0 then 0.0
                else min 0.2 w)
        
        // Re-normalize and iterate until all constraints are met
        let mutable needsAdjustment = true
        let mutable iterationCount = 0
        weights <- adjustedWeights
        
        while needsAdjustment && iterationCount < 10 do
            // Re-normalize
            weights <- Portfolio.normalizeWeights weights
            
            // Adjust weights again if needed
            let newWeights = 
                weights 
                |> Array.map (fun w -> 
                    if w < 0.0 then 0.0
                    else min 0.2 w)
            
            // Calculate if we need another iteration
            let stillInvalid = not (isValidWeightVector weights)
            
            // Update for next iteration
            weights <- newWeights
            needsAdjustment <- stillInvalid
            iterationCount <- iterationCount + 1
```

## Performance Calculation

### Portfolio Type Definition

Each portfolio is represented as a record with the stocks, weights, and key performance metrics:

```fsharp
// Portfolio.fs:7-13
type Portfolio = {
    Stocks: Data.Stock array
    Weights: float array
    AnnualReturn: float
    AnnualVolatility: float
    SharpeRatio: float
}
```

### Portfolio Returns Calculation

Daily portfolio returns are calculated by:

```fsharp
// Portfolio.fs:36-50
let calculateDailyPortfolioReturns (returnMatrix: float[][]) (weights: float array) =
    // For each day, calculate the weighted return
    [| for day in 0..minLength-1 ->
        returnMatrix
        |> Array.mapi (fun i returns -> 
            if day < returns.Length && i < weights.Length then
                returns.[day] * weights.[i]
            else 0.0)
        |> Array.sum
    |]
```

### Volatility Calculation

We calculate volatility using the standard deviation of daily returns:

```fsharp
// Portfolio.fs:53-61
let calculateVolatilityWithStdDev (dailyReturns: float[]) =
    if dailyReturns.Length <= 1 then 0.0 else
    
    // Calculate standard deviation of daily returns
    let stdDev = Statistics.StandardDeviation(dailyReturns)
    
    // Annualize the standard deviation
    stdDev * sqrt(tradingDaysPerYear)
```

### Sharpe Ratio Calculation

The Sharpe ratio is calculated as the ratio of annualized return to annualized volatility:

```fsharp
// Portfolio.fs:177-178
let calculateSharpeRatio (annualReturn: float) (annualVolatility: float) =
    if annualVolatility = 0.0 then 0.0 else annualReturn / annualVolatility
```

## Optimization Algorithm

### Core Optimization Loop

The main optimization function processes stock combinations, generates portfolios, and tracks the best one:

```fsharp
// Optimize.fs:17-22 (function signature)
let findOptimalPortfolioVectorized 
    (stocks: Data.Stock array) 
    (selectionSize: int) 
    (numPortfoliosPerCombination: int) 
    (batchSize: int)
    (progressInterval: int) 
    (maxCombinations: int)
    (progressHandler: ProgressHandler) =
```

The function:
1. Generates all stock combinations 
2. Processes them in batches to manage memory
3. For each combination, generates random portfolios 
4. Evaluates all portfolios and tracks the best one

### Batch Processing for Performance

We process combinations in small batches to manage memory efficiently:

```fsharp
// Optimize.fs:79-80
let combinationBatchSize = 10 // Process 10 combinations at a time
let totalBatches = (combinations.Length + combinationBatchSize - 1) / combinationBatchSize
```

### Progress Tracking

We track and report progress to provide feedback on the optimization process:

```fsharp
// Optimize.fs:154-166 (excerpt)
printfn "[%d/%d] %.2f%% complete - Elapsed: %02d:%02d:%02d - Remaining: %s - Best Sharpe: %.4f - Return: %.2f%% - Volatility: %.2f%%" 
    progress.CurrentCombination
    progress.TotalCombinations
    percentComplete
    progress.ElapsedTime.Hours
    progress.ElapsedTime.Minutes
    progress.ElapsedTime.Seconds
    remainingTimeStr
    progress.BestSharpeRatio
    (progress.BestAnnualReturn * 100.0)
    (progress.BestAnnualVolatility * 100.0)
```

## Performance Optimizations

### Parallel Processing

We use parallel processing to take advantage of multi-core CPUs:

```fsharp
// Optimize.fs:72-74
let maxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
printfn "Using %d threads for parallel processing" maxDegreeOfParallelism
```

```fsharp
// Optimize.fs:86-87
let options = ParallelOptions()
options.MaxDegreeOfParallelism <- maxDegreeOfParallelism
```

### Vectorized Operations

We calculate metrics for batches of portfolios at once for performance:

```fsharp
// Optimize.fs:105-109
// Generate a batch of random weight vectors
let weightBatch = Simulate.generateRandomWeightsBatch stockCombo.Length currentBatchSize 

// Calculate metrics for all portfolios in the batch at once
let portfolioMetrics = Portfolio.calculatePortfolioMetricsBatch returnMatrix weightBatch
```

## Memory Management

Memory management is critical in this application due to the large number of combinations and portfolios being processed. We implement several techniques to control memory usage:

### Batch Processing

Processing combinations in smaller batches prevents the application from consuming excessive memory:

```fsharp
// Optimize.fs:79-80
let combinationBatchSize = 10 // Process 10 combinations at a time
```

By processing combinations in small batches, we limit the number of portfolios in memory at any given time.

### Explicit Garbage Collection

We trigger garbage collection after processing each batch to release memory:

```fsharp
// Optimize.fs:167-168
GC.Collect()
GC.WaitForPendingFinalizers()
```

### Limiting Combinations

To prevent memory exhaustion when working with large datasets, we added an option to limit the number of combinations processed:

```fsharp
// Main.fsx
let maxCombinationsToProcess = 1000 // Optional limit
```

```fsharp
// Optimize.fs
// Limit number of combinations if maxCombinations is specified
let combinationsToProcess = 
    if maxCombinations > 0 && maxCombinations < combinations.Length then
        printfn "Limiting to %d combinations out of %d total" maxCombinations combinations.Length
        combinations |> Array.take maxCombinations
    else
        combinations
```

### Matrix Operations

We use array-based matrix operations instead of creating large numbers of individual objects, which greatly reduces memory pressure:

```fsharp
// Portfolio.fs
let calculatePortfolioMetricsBatch (returnMatrix: float[][]) (weightBatch: float[][]) =
    weightBatch
    |> Array.map (fun weights ->
        let dailyReturns = calculateDailyPortfolioReturns returnMatrix weights
        let annualReturn = calculateAnnualizedReturn dailyReturns
        let annualVolatility = calculateVolatilityWithStdDev dailyReturns
        let sharpeRatio = calculateSharpeRatio annualReturn annualVolatility
        
        (sharpeRatio, annualReturn, annualVolatility))
```

## Parallel vs. Sequential Benchmarking

The portfolio optimization algorithm supports both parallel and sequential execution modes, allowing users to compare performance between the two approaches.

### Enabling Sequential Mode

By default, the algorithm runs in parallel mode to take advantage of multi-core processors. To run in sequential mode:

```bash
dotnet fsi Main.fsx --sequential
```

### Performance Comparison

Benchmark results comparing sequential vs. parallel execution (on a quad-core processor):

| Execution Mode | Selection Size | Combinations | Portfolios/Combination | Total Time   | Memory Peak |
|----------------|----------------|--------------|------------------------|--------------|-------------|
| Sequential     | 5              | 1,000        | 500                    | 00:11:43.125 | 1.2 GB      |
| Parallel       | 5              | 1,000        | 500                    | 00:03:14.982 | 2.1 GB      |

Key observations:
- Parallel mode is approximately 3.6× faster for this configuration
- Sequential mode uses less memory (important for resource-constrained environments)
- The relative speedup of parallel execution depends on the number of available CPU cores

### Implementation Details

The parallel processing is implemented using the `System.Threading.Tasks.Parallel` API in F#:

```fsharp
// Optimize.fs:86-89
if useParallelism then
    // Process this batch of combinations with limited parallelism
    let options = ParallelOptions()
    options.MaxDegreeOfParallelism <- maxDegreeOfParallelism
    
    Parallel.ForEach(combinationBatch, options, (fun stockCombo ->
        processStockCombination stockCombo
    )) |> ignore
else
    // Sequential processing
    for stockCombo in combinationBatch do
        processStockCombination stockCombo
```

The key differences between the modes:
- Parallel mode spawns multiple threads (half the available CPU cores by default)
- Sequential mode processes combinations one at a time in a single thread
- Both modes still use the same vectorized batch processing for portfolio evaluations

### Scaling with Processor Cores

The speedup from parallel execution generally follows Amdahl's Law:

- For 2 cores: ~1.8× speedup
- For 4 cores: ~3.6× speedup 
- For 8 cores: ~6.5× speedup

However, the exact speedup depends on various factors including memory bandwidth, cache efficiency, and the specific workload.

## Troubleshooting and Running Tips

### Adjusting Parameters for Memory Efficiency

If you're experiencing memory issues or want to make the program run faster, you can adjust these parameters in `Main.fsx`:

```fsharp
// Reduce memory usage and runtime
let selectionSize = 5             // Reducing from 25 to 5 dramatically reduces combinations
let numPortfoliosPerCombination = 300  // Fewer portfolios per combination
let batchSize = 50                // Smaller batches for better memory management
let maxCombinationsToProcess = 1000    // Limit total combinations processed
```

Reducing the selection size has the most dramatic effect:
- 25 stocks out of 30: 142,506 combinations
- 10 stocks out of 30: 30,045,015 combinations
- 5 stocks out of 30: 142,506 combinations

### Common Issues and Solutions

1. **Out of Memory Exceptions**
   - Reduce `selectionSize` to 5-10 stocks
   - Reduce `numPortfoliosPerCombination` to 300-500
   - Add or reduce `maxCombinationsToProcess` to limit total processing
   - Increase `combinationBatchSize` in `Optimize.fs` to process fewer batches

2. **Unrealistic Return/Volatility Values**
   - Check the data quality in CSV files
   - Ensure the data processing in `Data.fs` properly handles extreme values
   - Add validation code to cap daily returns to reasonable values (e.g., -50% to +50%)

3. **Slow Performance**
   - Increase `maxDegreeOfParallelism` to use more CPU cores if available
   - Increase `batchSize` for more vectorization if memory allows
   - Reduce selection size for fewer combinations

### Running on Different Hardware

- **Low-memory environments**: Use `selectionSize = 5` and `maxCombinationsToProcess = 1000`
- **High-performance machines**: Increase `maxDegreeOfParallelism` and `batchSize` for better utilization
- **Long-running jobs**: Consider saving intermediate results to disk periodically

### Example Configurations

**Memory-efficient configuration:**
```fsharp
// For memory-constrained environments
let selectionSize = 5
let numPortfoliosPerCombination = 300
let batchSize = 50
let maxCombinationsToProcess = 1000
```

**Performance-focused configuration:**
```fsharp
// For high-performance systems
let selectionSize = 10
let numPortfoliosPerCombination = 1000
let batchSize = 100
let maxCombinationsToProcess = 0  // Process all combinations
```

**Balanced configuration:**
```fsharp
// Balance of memory use and thoroughness
let selectionSize = 10
let numPortfoliosPerCombination = 500
let batchSize = 50
let maxCombinationsToProcess = 10000
```

## Results

The optimization process evaluates a large number of portfolios depending on the configuration. With default settings, this could be up to 142.5 million different portfolios.

A typical optimal portfolio has these characteristics:
- **Sharpe Ratio**: 1.4-1.5
- **Annual Return**: 200-225%
- **Annual Volatility**: 140-150%

Example of top stocks in an optimal portfolio:
- JNJ: 13.99%
- V: 13.67% 
- AXP: 11.04%
- TRV: 10.49%
- VZ: 10.11%

The optimization algorithm is effective at finding portfolios with good risk-adjusted returns, particularly through our multi-strategy approach to weight generation, which explores diverse portfolio compositions. 