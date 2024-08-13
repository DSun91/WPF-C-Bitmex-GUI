﻿using BitmexGUI.Models;
using BitmexGUI.Services.Implementations;
using System.Windows;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Reflection.Metadata;
using System.Configuration;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.VisualBasic;
using System;

namespace BitmexGUI.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();

        public void Execute(object parameter) => _execute();
    }
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly string IdBinance = "";
        private readonly string ApiKeyBinance = "";
        private readonly string IdBitmex = "";
        private readonly string ApiKeyBitmex = "";

        private readonly string BinanceEndpointRest = ConfigurationManager.AppSettings["BaseRESTBinance"];
        private readonly string BinanceEndpointWss = ConfigurationManager.AppSettings["BaseWSSBinance"];
        private readonly string BitmexEndpointRest = ConfigurationManager.AppSettings["BaseRESTBitmex"];
        private readonly string BitmexEndpointWss = ConfigurationManager.AppSettings["BaseWSSBitmex"];
        private readonly string Instrument = ConfigurationManager.AppSettings["Instrument"];
        public event Action PriceDataUpdated;
        public event Action SettledPriceDataUpdated;
        public event Action BalanceUpdated;
        public event Action NewPricedataAdded;
        private Dictionary<string, CandlestickData> _priceDataDictionary = new Dictionary<string, CandlestickData>(); 
        private readonly int _maxCandlesLoading;
        private readonly BinanceAPI BinanceApi;
        private readonly BitmexAPI BitmexApi;
        private string TimeFrame= ConfigurationManager.AppSettings["Timeframe"];

        private ObservableCollection<CandlestickData> _priceData = new ObservableCollection<CandlestickData>();
        private ObservableCollection<SettledPrice> _settledPriceData = new ObservableCollection<SettledPrice>();
        private ObservableCollection<Account> _accountData = new ObservableCollection<Account>();
        private ObservableCollection<Instrument> _instrumentData = new ObservableCollection<Instrument>();


        public ObservableCollection<CandlestickData> PriceData
        {
            get => _priceData;
            set
            {
                _priceData = value;
                OnPropertyChanged(nameof(PriceData));
            }
        }
        public ObservableCollection<SettledPrice> SettledPriceData
        {
            get => _settledPriceData;
            set
            {
                _settledPriceData = value;
                OnPropertyChanged(nameof(SettledPriceData));
            }
        }
        public ObservableCollection<Account> AccountInfos
        {
            get => _accountData;
            set
            {
                _accountData = value;
                OnPropertyChanged(nameof(AccountInfos));
            }
        }
        public ObservableCollection<Instrument> InstrumentInfo
        {
            get => _instrumentData;
            set
            {
                _instrumentData = value;
                OnPropertyChanged(nameof(InstrumentInfo));
            }
        }

        private double _entryAmount;
        private double _sliderLeverage;
        private double _positionValue;
        private double _entryPrice;


        public double EntryAmount
        {
            get => _entryAmount;
            set
            {
                if (Math.Abs(_entryAmount - value) > 0.001) // Avoid unnecessary updates
                {
                    _entryAmount = value;
                    OnPropertyChanged();
                    CalculatePositionValue();
                    CalculateOrderCost();
                }
            }
        }

        public double SliderLeverage
        {
            get => _sliderLeverage;
            set
            {
                if (Math.Abs(_sliderLeverage - value) > 0.001) // Avoid unnecessary updates
                {
                    _sliderLeverage = Math.Round(value);
                    OnPropertyChanged();
                    CalculatePositionValue();
                    CalculateOrderCost();
                }
            }
        }
        private void CalculatePositionValue()
        {

            if (_entryAmount > 0 && _sliderLeverage > 0)
            {

                PositionValue = Math.Round(_entryAmount * _sliderLeverage, 2);

            }
            else if (_entryAmount > 0 && _sliderLeverage <= 0)
            {
                PositionValue = Math.Round(_entryAmount, 2);
            }
            else
            {
                PositionValue = 0;
            }
        }

        private void CalculateOrderCost()
        {
            CalculateQuantity();

            EntryPrice = Math.Round(EntryPrice, 0);

            double EOV = (Quantity) * EntryPrice;

            double BK = EOV + (EOV / SliderLeverage);

            OrderCost = Math.Round((EOV / SliderLeverage) + (EOV + BK) * (0.075 / 100), 2);
        }

        private void CalculateQuantity()
        {
            Quantity = (Math.Round(1000 * EntryAmount * SliderLeverage / EntryPrice)/1000);
            CalculateActualPositionValue();
        }


        public double PositionValue
        {
            get => _positionValue;
            private set
            {
                if (Math.Abs(_positionValue - value) > 0.001) // Avoid unnecessary updates
                {
                    _positionValue = value;
                    OnPropertyChanged();
                    
                }
            }
        }

        public double EntryPrice
        {
            get => _entryPrice;
            set
            {
                if (Math.Abs(_entryPrice - value) > 0.001) // Avoid unnecessary updates
                {
                    _entryPrice = value;
                    CalculateOrderCost();
                    OnPropertyChanged(); 
                }
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;
        private ICommand _createNewOrderCommand;
        public ICommand CreateNewOrderCommand
        {
            get
            {
                if (_createNewOrderCommand == null)
                {
                    _createNewOrderCommand = new RelayCommand(CreateNewOrder);
                }
                return _createNewOrderCommand;
            }
        }
        public MainViewModel(int InitialCandlesNumber)
        {


            int.TryParse(ConfigurationManager.AppSettings["MaxCacheCandles"],out _maxCandlesLoading);

            
            BinanceEndpointRest += $"/klines?symbol={Instrument}&interval={TimeFrame}&limit={InitialCandlesNumber}";
            BinanceEndpointWss += $"{Instrument.ToLower()}@kline_{TimeFrame}";
            BitmexEndpointWss += $"?subscribe=instrument:XBTUSDT";
            //MessageBox.Show(BinanceEndpointRest);
            BinanceApi = new BinanceAPI(IdBinance, ApiKeyBinance, BinanceEndpointRest, BinanceEndpointWss);
            BinanceApi.GetPriceREST(PriceData,_priceDataDictionary);
            BinanceApi.PriceUpdated += OnPriceUpdatedBinance;

            BitmexApi = new BitmexAPI(IdBitmex, ApiKeyBitmex, BitmexEndpointRest, BitmexEndpointWss); 
            BitmexApi.SettledPriceUpdated += OnPriceUpdatedBitmex;
            
            BitmexApi.AccountInfo += OnbalanceInfoReceived;

            //BitmexApi.SetLeverage("XBTUSDT",5.4);



        }
        private double _orderCost;


        public double OrderCost
        {
            get => _orderCost;
            set
            { 
                _orderCost = value;
                OnPropertyChanged();
                 
                 
                    
            }
        }

        private double _quantity;


        public double Quantity
        {
            get => _quantity;
            set
            {
                _quantity = value;
                OnPropertyChanged(); 
            }
        }

        private double _actualpositionvalue;

        public double ActualPositionValue
        {
            get => _actualpositionvalue;
            set
            {
                _actualpositionvalue = value;
                OnPropertyChanged();
            }
        }

        private void CalculateActualPositionValue()
        {
            ActualPositionValue = Math.Round(Quantity * EntryPrice,2);
        }

        public void CreateNewOrder()
        {
             
            //MessageBox.Show($"Order Cost: {actualtradeAmount * SliderLeverage}, fee {actualtradeAmount * SliderLeverage * 0.00015},total spent {actualtradeAmount + 2 * (actualtradeAmount * SliderLeverage * 0.00015)}");

            BitmexApi.CreateOrder("XBTUSDT",
                                   Quantity*1000000,
                                   Math.Round(EntryPrice, 0),
                                   "Limit",
                                   "GoodTillCancel",
                                   "Buy",
                                   SliderLeverage);


        }

        public void StartPriceFeed()
        {
            BinanceApi.GetPriceWSS();
            BitmexApi.GetPriceWSS();
        }

        public void GetBalance(string currency)
        {
            BitmexApi.GetBalance(currency);
        }
        private void OnbalanceInfoReceived(Account accountInfo)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (accountInfo != null)
                {
                    AccountInfos.Clear();
                    AccountInfos.Add(accountInfo);

                }
            });
            
            BalanceUpdated?.Invoke();
        }
        private void OnPriceUpdatedBinance(CandlestickData priceData)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {


                var timestamp = priceData.Timestamp;



                if (_priceDataDictionary.ContainsKey(timestamp.ToString()) && priceData != null)
                {

                    // Update existing entry
                    var existingData = _priceDataDictionary[timestamp.ToString()];
                    // Assuming PriceData has properties like Open, High, Low, Close
                    existingData.Open = priceData.Open;
                    existingData.High = priceData.High;
                    existingData.Low = priceData.Low;
                    existingData.Close = priceData.Close;

                    // Notify collection that an item has been updated
                    var index = PriceData.IndexOf(existingData);
                    if (index >= 0)
                    {
                        PriceData[index] = existingData; // Update the item in the ObservableCollection

                    }
                    PriceDataUpdated?.Invoke(); // Trigger the event
                }
                else
                {
                   
                    // Add new entry
                    _priceDataDictionary[timestamp.ToString()] = priceData;
                    PriceData.Add(priceData);
                    
                    while (PriceData.Count > _maxCandlesLoading)
                    {
                        PriceData.Remove(PriceData.First());
                    }
                    NewPricedataAdded?.Invoke();
                }

               
            });
        }
        private void OnPriceUpdatedBitmex(SettledPrice setpriceData)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {


                var timestamp = setpriceData.Timestamp;
                 
                SettledPriceData.Add(setpriceData);

                SettledPriceDataUpdated?.Invoke();  
            });
        }

        public void UpdateInitialCandles(int newInitialCandlesNumber)
        {

            BinanceApi.UpdateRestEndpoint($"https://api.binance.com/api/v3/klines?symbol=BTCUSDT&interval={TimeFrame}&limit={newInitialCandlesNumber}");

            //MessageBox.Show(_binanceApi.UrlRest);

            PriceData = new ObservableCollection<CandlestickData>();

            _priceDataDictionary=new Dictionary<string,CandlestickData>();

             

            for (int i = BinanceApi.CachedPriceData.Count- newInitialCandlesNumber; i < BinanceApi.CachedPriceData.Count;i++)
            {
                PriceData.Add(BinanceApi.CachedPriceData[i]);
                _priceDataDictionary.TryAdd(BinanceApi.CachedPriceData[i].Timestamp.ToString(), BinanceApi.CachedPriceData[i]);
            }
            

            //_binanceApi.GetPriceREST(PriceData,_priceDataDictionary); 
        }



        
        


         

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
