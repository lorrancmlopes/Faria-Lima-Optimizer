# pip install pandas numpy matplotlib seaborn
import os
import json
import glob
import pandas as pd
import numpy as np
import matplotlib.pyplot as plt
import seaborn as sns
from datetime import datetime

# Set style for plots
plt.style.use('seaborn-v0_8-whitegrid')
sns.set_palette("viridis")
plt.rcParams.update({'font.size': 12, 'figure.figsize': (12, 8)})

# Path to results directory
RESULTS_DIR = '../results'

def load_json_data(filepath):
    """Load portfolio data from JSON file"""
    with open(filepath, 'r') as f:
        return json.load(f)

def find_best_portfolio():
    """Find the portfolio with the highest Sharpe ratio from all results"""
    json_files = glob.glob(os.path.join(RESULTS_DIR, 'optimal_portfolio_*.json'))
    
    best_sharpe = -float('inf')
    best_portfolio = None
    
    for file in json_files:
        data = load_json_data(file)
        if data.get('SharpeRatio', 0) > best_sharpe:
            best_sharpe = data['SharpeRatio']
            best_portfolio = data
            best_file = file
    
    print(f"Best portfolio found in: {os.path.basename(best_file)}")
    print(f"Sharpe Ratio: {best_sharpe:.4f}")
    
    return best_portfolio

def plot_best_portfolio(portfolio_data):
    """Create a bar plot of stock weights in descending order"""
    if not portfolio_data:
        print("No portfolio data available")
        return
    
    # Extract stock data
    stocks = portfolio_data['Stocks']
    
    # Convert to DataFrame
    df = pd.DataFrame(stocks)
    
    # Convert weights to percentages and sort
    df['Weight'] = df['Weight'] * 100
    df = df.sort_values('Weight', ascending=False)
    
    # Create bar plot
    plt.figure(figsize=(14, 8))
    bar_plot = sns.barplot(x='Ticker', y='Weight', data=df)
    
    # Add percentage labels above each bar
    for i, v in enumerate(df['Weight']):
        if v >= 5:  # Only add labels for bars with significant percentage
            bar_plot.text(i, v + 0.5, f"{v:.1f}%", ha='center')
    
    plt.title(f'Optimal Portfolio Composition - Sharpe Ratio: {portfolio_data["SharpeRatio"]:.4f}')
    plt.ylabel('Weight (%)')
    plt.xlabel('Stock Ticker')
    plt.xticks(rotation=45)
    plt.tight_layout()
    
    # Save the plot
    plt.savefig(os.path.join(RESULTS_DIR, 'best_portfolio_weights.png'), dpi=300)
    plt.show()
    
    # Also print the top 5 stocks
    print("\nTop 5 stocks in optimal portfolio:")
    for _, row in df.head(5).iterrows():
        print(f"{row['Ticker']}: {row['Weight']:.2f}%")

def analyze_execution_times():
    """Compare execution times between parallel and sequential modes"""
    # Read benchmark log
    benchmark_file = os.path.join(RESULTS_DIR, 'benchmarks.log')
    
    if not os.path.exists(benchmark_file):
        print(f"Benchmark file not found: {benchmark_file}")
        return
    
    # Skip the header and read data
    df = pd.read_csv(benchmark_file)
    
    # Convert TimeMs to seconds
    df['TimeSec'] = df['TimeMs'] / 1000
    
    # Filter only entries with ExecutionMode 'parallel' or 'sequential'
    df = df[df['ExecutionMode'].isin(['parallel', 'sequential'])]
    
    # Check if we have enough data points
    if len(df) < 5:
        print(f"Not enough data points. Found {len(df)} benchmarks, need at least 5.")
        print("Please run more benchmarks.")
        return
    
    # Get summary statistics
    summary = df.groupby('ExecutionMode')['TimeSec'].agg(['count', 'mean', 'std', 'min', 'max'])
    summary['mean_min'] = summary['mean'] / 60  # convert to minutes
    
    print("\nExecution Time Summary (seconds):")
    print(summary[['count', 'mean', 'std', 'min', 'max']])
    
    print("\nExecution Time Summary (minutes):")
    print(f"Parallel: {summary.loc['parallel', 'mean'] / 60:.2f} minutes")
    print(f"Sequential: {summary.loc['sequential', 'mean'] / 60:.2f} minutes")
    
    # Calculate speedup
    if 'sequential' in summary.index and 'parallel' in summary.index:
        speedup = summary.loc['sequential', 'mean'] / summary.loc['parallel', 'mean']
        print(f"\nParallel speedup: {speedup:.2f}x faster")
    
    # Create boxplot
    plt.figure(figsize=(10, 6))
    sns.boxplot(x='ExecutionMode', y='TimeSec', data=df)
    plt.title('Execution Time Comparison: Parallel vs Sequential')
    plt.ylabel('Time (seconds)')
    plt.xlabel('Execution Mode')
    
    # Add exact means as text
    for i, mode in enumerate(['parallel', 'sequential']):
        if mode in df['ExecutionMode'].values:
            mean_time = df[df['ExecutionMode'] == mode]['TimeSec'].mean()
            plt.text(i, mean_time + 20, f"Mean: {mean_time:.1f}s", ha='center')
    
    plt.savefig(os.path.join(RESULTS_DIR, 'execution_time_comparison.png'), dpi=300)
    plt.show()
    
    # Create bar plot
    plt.figure(figsize=(10, 6))
    avg_times = df.groupby('ExecutionMode')['TimeSec'].mean()
    
    bars = plt.bar(avg_times.index, avg_times.values, color=['steelblue', 'darkorange'])
    
    # Add values on top of bars
    for bar in bars:
        height = bar.get_height()
        plt.text(bar.get_x() + bar.get_width()/2., height + 5,
                 f'{height:.1f}s\n({height/60:.1f}min)',
                 ha='center', va='bottom')
    
    plt.title('Average Execution Time Comparison')
    plt.ylabel('Time (seconds)')
    plt.xlabel('Execution Mode')
    plt.tight_layout()
    
    plt.savefig(os.path.join(RESULTS_DIR, 'avg_execution_time.png'), dpi=300)
    plt.show()

def analyze_portfolio_variations():
    """Analyze the variation in Sharpe ratios between runs"""
    json_files = glob.glob(os.path.join(RESULTS_DIR, 'optimal_portfolio_*.json'))
    
    data = []
    for file in json_files:
        portfolio = load_json_data(file)
        data.append({
            'ExecutionMode': portfolio.get('ExecutionMode', 'unknown'),
            'SharpeRatio': portfolio.get('SharpeRatio', 0),
            'TimeMs': portfolio.get('TimeElapsedMs', 0),
            'Timestamp': portfolio.get('Timestamp', '')
        })
    
    if not data:
        print("No portfolio data found")
        return
    
    df = pd.DataFrame(data)
    
    # Plot Sharpe ratio distribution
    plt.figure(figsize=(10, 6))
    sns.boxplot(x='ExecutionMode', y='SharpeRatio', data=df)
    plt.title('Sharpe Ratio Comparison: Parallel vs Sequential')
    plt.ylabel('Sharpe Ratio')
    plt.xlabel('Execution Mode')
    
    plt.savefig(os.path.join(RESULTS_DIR, 'sharpe_ratio_comparison.png'), dpi=300)
    plt.show()
    
    # Print statistics
    sharpe_stats = df.groupby('ExecutionMode')['SharpeRatio'].agg(['count', 'mean', 'std', 'min', 'max'])
    print("\nSharpe Ratio Statistics:")
    print(sharpe_stats)

def main():
    print("Portfolio Optimization Analysis")
    print("==============================")
    
    # Find and plot best portfolio
    best_portfolio = find_best_portfolio()
    plot_best_portfolio(best_portfolio)
    
    # Analyze execution times
    print("\nAnalyzing execution times...")
    analyze_execution_times()
    
    # Analyze portfolio variations
    print("\nAnalyzing portfolio variations...")
    analyze_portfolio_variations()
    
    print("\nAnalysis complete. Plots saved to", RESULTS_DIR)

if __name__ == "__main__":
    main() 