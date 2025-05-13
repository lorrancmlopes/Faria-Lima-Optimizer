# F# Portfolio Optimization Project

This project implements a portfolio optimization simulator in F# using functional programming principles, parallelism, and real financial data from the Dow Jones stocks.

## Objective

The goal is to identify the optimal long-only portfolio of stocks from the Dow Jones index that maximizes the Sharpe Ratio, using historical price data.

## Project Structure

```
/project-root
  ├── data/
  │   └── dow_jones_close_prices_aug_dec_2024.csv
  |   └── downloader.py 
  ├── results/                                     
  ├── Data.fs         # Load CSV, compute returns
  ├── Portfolio.fs    # Return, volatility, Sharpe calculations  
  ├── Simulate.fs     # Generate valid random weight vectors
  ├── Optimize.fs     # Parallel simulation & best portfolio selection
  ├── Main.fsx        # Entry point with progress logging
  └── README.md       # This file
```

## Implementation Details

The simulator performs the following steps:

1. Load stock price data from CSV and compute daily discrete returns
2. Generate all combinations of 25 out of 30 stocks 
3. For each combination:
   - Simulate multiple valid portfolios (weight vectors)
   - All weights ≥ 0 and ≤ 0.2
   - Weights sum to 1
4. Evaluate each portfolio by calculating:
   - Portfolio return vector
   - Annualized return
   - Annualized volatility
   - Sharpe Ratio (disregarding risk-free rate)
5. Find the portfolio with the highest Sharpe Ratio

## Functional Programming Features

- Pure functions (no side effects)
- Immutability
- Function composition and pipelines
- Higher-order functions like map, filter
- Pattern matching
- Option types for safely handling missing data

## Parallelism Implementation

The project offers two execution modes:

- **Parallel mode**: Uses multiple CPU cores for faster processing
- **Sequential mode**: Processes combinations one at a time, using less memory

Both modes use vectorized calculations for portfolio metrics to optimize performance.

## Running the Project

### Prerequisites

Before running the project:

## F# + C# Project Installation

1. **Install .NET 9.0**
   Download and install from:
   [https://dotnet.microsoft.com/en-us/download/dotnet/9.0](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)

### Configuration

The project can be configured by modifying parameters in `Main.fsx`:

```fsharp
// Key parameters to adjust
let selectionSize = 20                   // Number of stocks to select from 30
let numPortfoliosPerCombination = 1000    // Portfolios to generate per combination
let batchSize = 50                       // Portfolios to process at once
let maxCombinationsToProcess = 0      // Limit total combinations (0 = process all)
```

### Execution Modes

The program supports two execution modes:

#### Parallel Mode (Default)
```bash
dotnet fsi Main.fsx
```

#### Sequential Mode
```bash
dotnet fsi Main.fsx --sequential
```

## Output

The program outputs:
- Best Sharpe Ratio achieved
- Portfolio weights for each stock
- Selected stocks
- Total execution time
- CSV and JSON files with results in the `results` directory

## Dependencies

- F# 
- MathNet.Numerics 5.0.0 

