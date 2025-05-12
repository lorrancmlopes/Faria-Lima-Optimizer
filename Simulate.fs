
module Simulate

open System

/// Random number generator
let private random = Random()

/// Checks if a weight vector is valid (sum to 1 and all between 0 and 0.2)
let isValidWeightVector (weights: float array) =
    let sum = Array.sum weights
    let inRange = weights |> Array.forall (fun w -> w >= 0.0 && w <= 0.2)
    abs(sum - 1.0) < 0.0001 && inRange

/// Generates a random weight value between 0 and 0.2
let private generateRandomWeight() =
    random.NextDouble() * 0.2

/// Generates all combinations of 25 out of 30 stocks
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

/// Generates random weight vectors with constraints:
/// 1. All weights must be between 0 and 0.2
/// 2. Weights must sum to 1.0
let generateRandomWeights (numStocks: int) (numPortfolios: int) =
    [| 
        for _ in 1..numPortfolios do
            // Generate initial random weights between 0 and 0.2
            let weights = Array.init numStocks (fun _ -> generateRandomWeight())
            
            // Normalize to ensure they sum to 1.0
            let normalizedWeights = Portfolio.normalizeWeights weights
            
            // If any weight exceeds 0.2 after normalization, we need to adjust
            let mutable validWeights = normalizedWeights
            let mutable attempts = 0
            
            while (not (Portfolio.isValidWeightVector validWeights)) && attempts < 100 do
                // Try a different approach: generate new weights but cap at 0.2
                let newWeights = Array.init numStocks (fun _ -> 
                    min 0.2 (generateRandomWeight()))
                validWeights <- Portfolio.normalizeWeights newWeights
                attempts <- attempts + 1
            
            // In case we still don't have valid weights, use equal weights
            if not (Portfolio.isValidWeightVector validWeights) then
                Array.create numStocks (1.0 / float numStocks)
            else
                validWeights
    |]

/// Generate a random weight vector using one of several strategies to improve search diversity
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
                0.05 + random.NextDouble() * 0.15   // 5% to 20%
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

/// Create a valid weight vector by applying constraints and normalization
let private createValidWeightVector (numStocks: int) (maxAttempts: int) =
    let mutable attempts = 0
    let mutable weights = generateOneRandomWeightVector numStocks
    let mutable isValid = false
    
    // Try to find valid weights respecting all constraints
    while not isValid && attempts < maxAttempts do
        // Normalize the weights to sum to 1.0
        weights <- Portfolio.normalizeWeights weights
        
        // Check if any weight exceeds maximum after normalization
        if not (isValidWeightVector weights) then
            // Any negative weights are set to 0, weights above maximum are capped
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
            
            // If we still haven't found valid weights, try a different starting point
            if needsAdjustment then
                weights <- generateOneRandomWeightVector numStocks
        else
            isValid <- true
        
        attempts <- attempts + 1
    
    // If we failed to find valid weights, use equal weights
    if not isValid then
        Array.create numStocks (1.0 / float numStocks)
    else
        weights

/// Generates a batch of random weights as a matrix (each row is a weight vector)
/// Improved to better explore the full range of possible weight distributions
let generateRandomWeightsBatch (numStocks: int) (batchSize: int) =
    // Create a batch of weight vectors, ensuring each one meets all constraints
    Array.init batchSize (fun _ -> createValidWeightVector numStocks 5) 