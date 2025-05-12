module Portfolio

open MathNet.Numerics.LinearAlgebra
open MathNet.Numerics.LinearAlgebra.Double
open MathNet.Numerics.Statistics
open System

/// Represents a portfolio with stocks and their respective weights
type Portfolio = {
    Stocks: Data.Stock array
    Weights: float array
    AnnualReturn: float
    AnnualVolatility: float
    SharpeRatio: float
}

/// The number of trading days in a year (used for annualization)
let private tradingDaysPerYear = 252.0

/// The annual risk-free rate (used for Sharpe ratio calculation)
let private riskFreeRate = 0.0 // Changed to 0% as required

/// Calculates the covariance matrix for the returns manually
let calculateCovarianceMatrixManually (data: float[][]) =
    let n = data.Length
    if n = 0 then Matrix<float>.Build.Dense(0, 0) else
    
    // Get means for each series
    let means = Array.map (fun (series: float[]) -> 
        if series.Length = 0 then 0.0 else Array.average series) data
    
    // Create covariance matrix
    let cov = Matrix<float>.Build.Dense(n, n)
    
    for i in 0..n-1 do
        for j in 0..n-1 do
            let series1 = data.[i]
            let series2 = data.[j]
            let mean1 = means.[i]
            let mean2 = means.[j]
            
            let minLength = min series1.Length series2.Length
            
            if minLength > 1 then
                let covariance = 
                    [| for k in 0..minLength-1 -> 
                        (series1.[k] - mean1) * (series2.[k] - mean2) |]
                    |> Array.sum
                    |> fun sum -> sum / float(minLength - 1)
                
                cov.[i, j] <- covariance
            else
                cov.[i, j] <- 0.0
    
    cov

/// Calculate portfolio daily returns from returns matrix and weights
let calculateDailyPortfolioReturns (returnMatrix: float[][]) (weights: float array) =
    // Ensure all return series have the same length
    let lengths = returnMatrix |> Array.map Array.length
    if lengths.Length = 0 then [||] else
    
    let minLength = lengths |> Array.min
    
    // For each day, calculate the weighted return
    [| for day in 0..minLength-1 ->
        returnMatrix
        |> Array.mapi (fun i returns -> 
            if day < returns.Length && i < weights.Length then
                returns.[day] * weights.[i]
            else 0.0)
        |> Array.sum
    |]
  
/// Calculate volatility using standard deviation approach directly
let calculateVolatilityWithStdDev (dailyReturns: float[]) =
    if dailyReturns.Length <= 1 then 0.0 else
    
    // Calculate standard deviation of daily returns
    let stdDev = Statistics.StandardDeviation(dailyReturns)
    
    // Annualize the standard deviation
    stdDev * sqrt(tradingDaysPerYear)

/// Calculate annual returns, volatility and Sharpe ratio for multiple portfolios at once using vectorized operations
/// This is much more efficient than calculating one portfolio at a time
let calculatePortfolioMetricsBatch (returnMatrix: float[][]) (weightMatrices: float[][]) =
    if returnMatrix.Length = 0 || weightMatrices.Length = 0 then
        [||]
    else
        try
            // Calculate mean returns (annualized)
            let meanReturns = 
                returnMatrix 
                |> Array.map (fun returns -> 
                    if returns.Length = 0 then 0.0
                    else Array.average returns * tradingDaysPerYear)
            
            // Debug info disabled - uncomment if needed for troubleshooting
            (*
            if Random().Next(0, 1000) = 0 then
                let maxMeanReturn = if meanReturns.Length > 0 then Array.max meanReturns else 0.0
                let minMeanReturn = if meanReturns.Length > 0 then Array.min meanReturns else 0.0
                printfn "Mean returns range: %.6f to %.6f" minMeanReturn maxMeanReturn
            *)
            
            // Create the mean returns vector
            let meanReturnsVector = DenseVector.OfArray(meanReturns)
            
            // Calculate covariance matrix (annualized)
            let covMatrix = 
                if returnMatrix.Length > 1 then
                    // Use manual implementation
                    let covMatrixManual = calculateCovarianceMatrixManually returnMatrix
                    
                    // Debug info disabled - uncomment if needed for troubleshooting
                    (*
                    if Random().Next(0, 1000) = 0 then
                        let mutable maxCov = 0.0
                        let mutable minCov = 0.0
                        if covMatrixManual.RowCount > 0 && covMatrixManual.ColumnCount > 0 then
                            maxCov <- covMatrixManual.Enumerate() |> Seq.max
                            minCov <- covMatrixManual.Enumerate() |> Seq.min
                        printfn "Covariance range: %.6f to %.6f" minCov maxCov
                    *)
                    
                    covMatrixManual * tradingDaysPerYear
                else
                    DenseMatrix.OfArray(Array2D.create 1 1 0.0)
            
            // Transpose the return matrix for calculating daily portfolio returns
            let numberOfDays = returnMatrix |> Array.map Array.length |> Array.min
            
            // Calculate metrics for each weight vector
            weightMatrices
            |> Array.map (fun weights ->
                let weightsVector = DenseVector.OfArray(weights)
                
                // Calculate portfolio return (μ = w^T * r)
                let portfolioReturn = weightsVector.DotProduct(meanReturnsVector)
                
                // Calculate daily portfolio returns for standard deviation approach
                let dailyPortfolioReturns = calculateDailyPortfolioReturns returnMatrix weights
                
                // Calculate portfolio volatility using standard deviation of daily returns
                let portfolioVolatility = calculateVolatilityWithStdDev dailyPortfolioReturns
                
                // Calculate Sharpe ratio (with zero risk-free rate)
                let sharpeRatio = if portfolioVolatility = 0.0 then 0.0 else portfolioReturn / portfolioVolatility
                
                (sharpeRatio, portfolioReturn, portfolioVolatility)
            )
        with
        | ex ->
            printfn "Error calculating batch metrics: %s" ex.Message
            [||]

/// Calculates the portfolio return vector given returns matrix and weights
/// R_port = R_matrix × w
let calculatePortfolioReturns (returnMatrix: float[][]) (weights: float array) =
    if returnMatrix.Length = 0 then
        [| |] // Return empty array if returnMatrix is empty
    else
        // Ensure all rows have the same length
        let rowLengths = returnMatrix |> Array.map (fun row -> row.Length)
        let minLength = if rowLengths.Length > 0 then Array.min rowLengths else 0
        
        // Create the return vector
        [|
            for i in 0..(minLength-1) do
                let mutable sum = 0.0
                for j in 0..(returnMatrix.Length-1) do
                    if j < weights.Length && i < returnMatrix.[j].Length then
                        sum <- sum + weights.[j] * returnMatrix.[j].[i]
                yield sum
        |]

/// Calculates the annualized portfolio return
/// μ = mean(R_port) × 252
let calculateAnnualReturn (portfolioReturns: float array) =
    if portfolioReturns.Length = 0 then
        0.0 // Return 0 if portfolioReturns is empty
    else
        let meanDailyReturn = Statistics.Mean(portfolioReturns)
        meanDailyReturn * tradingDaysPerYear

/// Calculates the annualized portfolio volatility
/// σ = std(R_port) × sqrt(252)
let calculateAnnualVolatility (returnMatrix: float[][]) (weights: float array) =
    if returnMatrix.Length = 0 || weights.Length = 0 then
        0.0 // Return 0 if returnMatrix or weights is empty
    else
        try
            // Calculate daily portfolio returns
            let dailyReturns = calculatePortfolioReturns returnMatrix weights
            
            // Calculate volatility using standard deviation approach
            calculateVolatilityWithStdDev dailyReturns
        with
        | ex ->
            printfn "Error calculating volatility: %s" ex.Message
            0.0 // Return 0 in case of error

/// Calculates the Sharpe Ratio
/// SR = μ / σ (zero risk-free rate)
let calculateSharpeRatio (annualReturn: float) (annualVolatility: float) =
    if annualVolatility = 0.0 then 0.0 else annualReturn / annualVolatility

/// Creates a portfolio with the given stocks and weights
let createPortfolio (stocks: Data.Stock array) (weights: float array) =
    try
        let returnMatrix = Data.createReturnsMatrix stocks
        let portfolioReturns = calculatePortfolioReturns returnMatrix weights
        let annualReturn = calculateAnnualReturn portfolioReturns
        let annualVolatility = calculateAnnualVolatility returnMatrix weights
        let sharpeRatio = calculateSharpeRatio annualReturn annualVolatility
        
        {
            Stocks = stocks
            Weights = weights
            AnnualReturn = annualReturn
            AnnualVolatility = annualVolatility
            SharpeRatio = sharpeRatio
        }
    with
    | ex ->
        printfn "Error creating portfolio: %s" ex.Message
        {
            Stocks = stocks
            Weights = weights
            AnnualReturn = 0.0
            AnnualVolatility = 1.0
            SharpeRatio = 0.0
        }

/// Evaluates if the weights are valid (sum to 1 and all between 0 and 0.2)
let isValidWeightVector (weights: float array) =
    let sum = Array.sum weights
    let inRange = weights |> Array.forall (fun w -> w >= 0.0 && w <= 0.2)
    abs(sum - 1.0) < 0.0001 && inRange

/// Normalizes weights to ensure they sum to 1.0
let normalizeWeights (weights: float array) =
    let sum = Array.sum weights
    if sum > 0.0 then
        weights |> Array.map (fun w -> w / sum)
    else
        Array.create weights.Length (1.0 / float weights.Length) 