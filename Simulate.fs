module Simulate

open System

/// Random number generator
let private random = Random()

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

/// Generates a batch of random weights as a matrix (each row is a weight vector)
/// This is much more efficient for vectorized calculations
let generateRandomWeightsBatch (numStocks: int) (batchSize: int) =
    // Create a matrix of random weights between 0 and 0.2
    let weightMatrix = 
        Array.init batchSize (fun _ -> 
            Array.init numStocks (fun _ -> random.NextDouble() * 0.2))
    
    // Normalize each row to sum to 1
    weightMatrix
    |> Array.map (fun weights -> 
        let sum = Array.sum weights
        if sum > 0.0 then
            Array.map (fun w -> w / sum) weights
        else
            Array.create numStocks (1.0 / float numStocks))
    
    // Now we need to handle the case where weights might be > 0.2 after normalization
    |> Array.map (fun weights ->
        if Array.exists (fun w -> w > 0.2) weights then
            // Need to cap and renormalize - simpler approach for batch processing
            let cappedWeights = Array.map (fun w -> min 0.2 w) weights
            let sum = Array.sum cappedWeights
            if sum > 0.0 then
                Array.map (fun w -> w / sum) cappedWeights
            else
                Array.create numStocks (1.0 / float numStocks)
        else
            weights) 