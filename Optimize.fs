module Optimize

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks

/// Tracks optimization progress
type OptimizationProgress = {
    TotalCombinations: int
    CurrentCombination: int
    ElapsedTime: TimeSpan
    EstimatedTimeRemaining: TimeSpan option
    BestSharpeRatio: float
    BestAnnualReturn: float
    BestAnnualVolatility: float
    BestPortfolio: Portfolio.Portfolio option
}

/// Handler for progress updates
type ProgressHandler = OptimizationProgress -> unit

/// Find the optimal portfolio among all stock combinations using vectorized operations
let findOptimalPortfolioVectorized 
    (stocks: Data.Stock array) 
    (selectionSize: int) 
    (numPortfoliosPerCombination: int) 
    (batchSize: int)
    (progressInterval: int) 
    (maxCombinations: int)
    (progressHandler: ProgressHandler) =
    
    printfn "Using vectorized optimization with batch size: %d" batchSize
    
    // Generate all stock combinations
    let allCombinations = Simulate.generateStockCombinations stocks selectionSize
    let totalOriginalCombinations = allCombinations.Length
    
    // Limit the number of combinations to process
    let combinations = 
        if maxCombinations > 0 && maxCombinations < totalOriginalCombinations then
            printfn "Limiting to %d combinations out of %d possible combinations" maxCombinations totalOriginalCombinations
            allCombinations |> Array.take maxCombinations
        else
            allCombinations
    
    let totalCombinations = combinations.Length
    printfn "Processing %d combinations of %d stocks" totalCombinations selectionSize
    
    // Start the timer for progress tracking
    let stopwatch = Stopwatch.StartNew()
    let mutable bestPortfolio = None
    let mutable bestSharpeRatio = Double.MinValue
    let mutable bestAnnualReturn = 0.0
    let mutable bestAnnualVolatility = 0.0
    let mutable processedCombinations = 0
    
    // Use a lockable object for thread-safe updates
    let lockObj = Object()
    
    // Determine the degree of parallelism based on available processors
    // Use fewer cores to avoid excessive memory usage
    let maxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
    printfn "Using %d threads for parallel processing" maxDegreeOfParallelism
    
    // Process combinations in batches to reduce memory pressure
    let combinationBatchSize = 10 // Process 10 combinations at a time
    let totalBatches = (combinations.Length + combinationBatchSize - 1) / combinationBatchSize
    
    // Track when the last progress report was made
    let mutable lastProgressTime = DateTime.Now
    
    for batchIdx in 0..(totalBatches - 1) do
        // Get the current batch of combinations
        let startIdx = batchIdx * combinationBatchSize
        let endIdx = min (startIdx + combinationBatchSize) combinations.Length
        let combinationBatch = combinations.[startIdx..endIdx-1]
        
        // Process this batch of combinations with limited parallelism
        let options = ParallelOptions()
        options.MaxDegreeOfParallelism <- maxDegreeOfParallelism
        
        Parallel.ForEach(combinationBatch, options, (fun stockCombo ->
            // Get the return matrix for this stock combination
            let returnMatrix = Data.createReturnsMatrix stockCombo
            
            // Initialize variables to track the best portfolio for this combination
            let mutable localBestSharpeRatio = Double.MinValue
            let mutable localBestWeights = [||]
            let mutable localBestAnnualReturn = 0.0
            let mutable localBestAnnualVolatility = 0.0
            
            // Process portfolios in batches for better performance
            let batchesToProcess = (numPortfoliosPerCombination + batchSize - 1) / batchSize
            
            for batchIndex in 0..batchesToProcess-1 do
                // Determine the size of this batch
                let currentBatchSize = min batchSize (numPortfoliosPerCombination - batchIndex * batchSize)
                if currentBatchSize <= 0 then () else
                
                // Generate a batch of random weight vectors
                let weightBatch = Simulate.generateRandomWeightsBatch stockCombo.Length currentBatchSize 
                
                // Calculate metrics for all portfolios in the batch at once
                let portfolioMetrics = Portfolio.calculatePortfolioMetricsBatch returnMatrix weightBatch
                
                // Find the best portfolio in this batch
                if portfolioMetrics.Length > 0 then
                    let bestBatchIndex, (bestBatchSharpe, bestBatchReturn, bestBatchVol) =
                        portfolioMetrics 
                        |> Array.indexed 
                        |> Array.maxBy (fun (_, (sharpe, _, _)) -> sharpe)
                    
                    // Update local best if this batch has a better portfolio
                    if bestBatchSharpe > localBestSharpeRatio then
                        localBestSharpeRatio <- bestBatchSharpe
                        localBestWeights <- weightBatch.[bestBatchIndex]
                        localBestAnnualReturn <- bestBatchReturn
                        localBestAnnualVolatility <- bestBatchVol
            
            // Thread-safe update of the best overall portfolio
            lock lockObj (fun () ->
                if localBestSharpeRatio > bestSharpeRatio then
                    bestSharpeRatio <- localBestSharpeRatio
                    bestAnnualReturn <- localBestAnnualReturn
                    bestAnnualVolatility <- localBestAnnualVolatility
                    bestPortfolio <- Some {
                        Portfolio.Stocks = stockCombo
                        Portfolio.Weights = localBestWeights
                        Portfolio.AnnualReturn = localBestAnnualReturn
                        Portfolio.AnnualVolatility = localBestAnnualVolatility
                        Portfolio.SharpeRatio = localBestSharpeRatio
                    }
                    
                // Update progress counter
                processedCombinations <- processedCombinations + 1
                
                // Report progress at specified intervals or if enough time has passed
                let now = DateTime.Now
                let timeSinceLastReport = now - lastProgressTime
                
                if processedCombinations % progressInterval = 0 || 
                   processedCombinations = totalCombinations ||
                   timeSinceLastReport.TotalMinutes >= 5.0 then
                    
                    lastProgressTime <- now
                    let elapsed = stopwatch.Elapsed
                    
                    // Estimate remaining time
                    let estimatedTimeRemaining =
                        if processedCombinations > 0 then
                            let timePerCombination = elapsed.TotalMilliseconds / float processedCombinations
                            let remainingCombinations = totalCombinations - processedCombinations
                            let remainingTimeMs = timePerCombination * float remainingCombinations
                            Some(TimeSpan.FromMilliseconds(remainingTimeMs))
                        else
                            None
                    
                    // Call the progress handler
                    progressHandler {
                        TotalCombinations = totalCombinations
                        CurrentCombination = processedCombinations
                        ElapsedTime = elapsed
                        EstimatedTimeRemaining = estimatedTimeRemaining
                        BestSharpeRatio = bestSharpeRatio
                        BestAnnualReturn = bestAnnualReturn
                        BestAnnualVolatility = bestAnnualVolatility
                        BestPortfolio = bestPortfolio
                    }
            )
        )) |> ignore
        
        // Force garbage collection after each batch to free memory
        GC.Collect()
        GC.WaitForPendingFinalizers()
        
        // Only report time-based progress at batch level
        let currentBatchProgress = (batchIdx + 1) * 100 / totalBatches
        let timeElapsed = stopwatch.Elapsed
        let timePerBatch = timeElapsed.TotalSeconds / float (batchIdx + 1)
        let remainingBatches = totalBatches - (batchIdx + 1)
        let estimatedRemainingSeconds = timePerBatch * float remainingBatches
        let estimatedRemainingTime = TimeSpan.FromSeconds(estimatedRemainingSeconds)
        
        if batchIdx % 100 = 0 || batchIdx = totalBatches - 1 then
            printfn "Completed batch %d/%d (%.1f%%) - Elapsed: %s - Est. remaining: %s" 
                (batchIdx + 1) 
                totalBatches 
                (float currentBatchProgress)
                (timeElapsed.ToString(@"hh\:mm\:ss"))
                (estimatedRemainingTime.ToString(@"hh\:mm\:ss"))
    
    // Return the best portfolio found
    bestPortfolio

/// Default progress handler that prints to console
let defaultProgressHandler (progress: OptimizationProgress) =
    let percentComplete = 100.0 * float progress.CurrentCombination / float progress.TotalCombinations
    
    let remainingTimeStr = 
        match progress.EstimatedTimeRemaining with
        | Some timeRemaining -> 
            sprintf "%02d:%02d:%02d" 
                timeRemaining.Hours 
                timeRemaining.Minutes 
                timeRemaining.Seconds
        | None -> "calculating..."
    
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