﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
using NAudio.Wave;
using TaxiCallCenter.MVP.WpfApp.Client;
using TaxiCallCenter.MVP.WpfApp.Events;
using TaxiCallCenter.MVP.WpfApp.Extensions;
using TaxiCallCenter.MVP.WpfApp.Models;

namespace TaxiCallCenter.MVP.WpfApp
{
    public class MainViewModel : BaseViewModel
    {
        private readonly Window window;
        private readonly SpeechKitClient speechKitClient = new SpeechKitClient("0fb959a9-2236-4e00-a13b-74f9f3b78a14");
        private readonly Guid userId = Guid.NewGuid();

        private String speechTopic = "queries";
        private AudioDevice selectedInputDevice;
        private AudioDevice selectedOutputDevice;
        private TtsSpeaker selectedSpeaker;
        private TtsEmotion selectedEmotion;

        public MainViewModel(Window window)
        {
            this.window = window;

            this.AudioRecorder = new AudioRecorder(this);
            this.AudioPlayer = new AudioPlayer(this);
            this.AudioSaver = new AudioSaver();

            this.AudioRecorder.RecordingComplete += this.AudioRecorderOnRecordingComplete;

            var waveInDevices = WaveIn.DeviceCount;
            for (var deviceId = 0; deviceId < waveInDevices; deviceId++)
            {
                var deviceInfo = WaveIn.GetCapabilities(deviceId);
                this.InputDevices.Add(new AudioDevice
                {
                    Id = deviceId,
                    Name = deviceInfo.ProductName
                });
                ////this.Log.LogEvent($"Device {deviceId}: {deviceInfo.ProductName}, {deviceInfo.Channels} channels");
            }

            this.SelectedInputDevice = this.InputDevices.FirstOrDefault();

            var waveOutDevices = WaveOut.DeviceCount;
            for (var deviceId = 0; deviceId < waveOutDevices; deviceId++)
            {
                var deviceInfo = WaveOut.GetCapabilities(deviceId);
                this.OutputDevices.Add(new AudioDevice
                {
                    Id = deviceId,
                    Name = deviceInfo.ProductName
                });
                ////this.Log.LogEvent($"Device {deviceId}: {deviceInfo.ProductName}, {deviceInfo.Channels} channels");
            }

            this.SelectedOutputDevice = this.OutputDevices.FirstOrDefault();

            this.Speakers.Add(new TtsSpeaker { Name = "jane" });
            this.Speakers.Add(new TtsSpeaker { Name = "oksana" });
            this.Speakers.Add(new TtsSpeaker { Name = "alyss" });
            this.Speakers.Add(new TtsSpeaker { Name = "omazh" });
            this.SelectedSpeaker = this.Speakers[1];

            this.Emotions.Add(new TtsEmotion { Name = "good" });
            this.Emotions.Add(new TtsEmotion { Name = "evil" });
            this.Emotions.Add(new TtsEmotion { Name = "neutral" });
            this.SelectedEmotion = this.Emotions[0];

            this.OrderStateMachine = new OrderStateMachine(new SpeechSubsystem(this), new Logger(this), new OrdersService(this));
        }

        private async void AudioRecorderOnRecordingComplete(Object sender, RecordingCompleteEventArgs e)
        {
            //this.AudioPlayer.PlayBytes(e.RecordingBytes);
            this.Log.LogEvent("Sending data for recognition");
            var recognitionResults = await this.RecognizeAsync(e.RecordingBytes);
            if (recognitionResults.Success && recognitionResults.Variants.Any())
            {
                var result = recognitionResults.Variants.First();
                this.Log.LogEvent($"Recognized text '{result.Text}' (confidence - {result.Confidence:N4})");
                await this.OrderStateMachine.ProcessResponseAsync(result.Text);
            }
            else
            {
                this.Log.LogEvent($"Recognition failed");
                await this.OrderStateMachine.ProcessRecognitionFailure();
            }
        }

        public ObservableCollection<LogEntry> Log { get; } = new ObservableCollection<LogEntry>();

        public ObservableCollection<AudioDevice> InputDevices { get; } = new ObservableCollection<AudioDevice>();

        public ObservableCollection<AudioDevice> OutputDevices { get; } = new ObservableCollection<AudioDevice>();

        public ObservableCollection<TtsSpeaker> Speakers { get; } = new ObservableCollection<TtsSpeaker>();

        public ObservableCollection<TtsEmotion> Emotions { get; } = new ObservableCollection<TtsEmotion>();

        public AudioDevice SelectedInputDevice
        {
            get => this.selectedInputDevice;
            set
            {
                if (Equals(value, this.selectedInputDevice)) return;
                this.OnPropertyChanging();
                this.selectedInputDevice = value;
                this.OnPropertyChanged();
            }
        }

        public AudioDevice SelectedOutputDevice
        {
            get => this.selectedOutputDevice;
            set
            {
                if (Equals(value, this.selectedOutputDevice)) return;
                this.OnPropertyChanging();
                this.selectedOutputDevice = value;
                this.OnPropertyChanged();
            }
        }

        public TtsSpeaker SelectedSpeaker
        {
            get => this.selectedSpeaker;
            set
            {
                if (Equals(value, this.selectedSpeaker)) return;
                this.OnPropertyChanging();
                this.selectedSpeaker = value;
                this.OnPropertyChanged();
            }
        }

        public TtsEmotion SelectedEmotion
        {
            get => this.selectedEmotion;
            set
            {
                if (Equals(value, this.selectedEmotion)) return;
                this.OnPropertyChanging();
                this.selectedEmotion = value;
                this.OnPropertyChanged();
            }
        }

        public AudioRecorder AudioRecorder { get; }

        public AudioPlayer AudioPlayer { get; }

        public AudioSaver AudioSaver { get; }

        public OrderStateMachine OrderStateMachine { get; set; }

        public async Task SpeakAsync(String text)
        {
            this.Log.LogEvent($"Syntesizing text '{text}'");
            var audio = await this.speechKitClient.GenerateAsync(this.SelectedSpeaker.Name, this.SelectedEmotion.Name, text);
            this.Log.LogEvent($"Received syntesized text: {audio.Length} bytes");
            this.AudioSaver.SaveBytes(this.userId, "Syntesized", Guid.NewGuid(), audio);
            this.AudioPlayer.PlayBytes(audio);
        }

        public async Task<RecognitionResults> RecognizeAsync(Byte[] audioBytes)
        {
            var result = await this.speechKitClient.RecognizeAsync(this.userId, this.speechTopic, audioBytes);
            var xml = XElement.Parse(result);
            if (xml.Name != "recognitionResults")
            {
                throw new InvalidOperationException();
            }

            var results = new RecognitionResults();
            if (xml.Attribute("success")?.Value == "1")
            {
                results.Success = true;
                foreach (var element in xml.Elements("variant"))
                {
                    results.Variants.Add(new RecognitionVariant
                    {
                        Confidence = Double.Parse(element.Attribute("confidence")?.Value ?? "0"),
                        Text = element.Value.Trim()
                    });
                }
            }

            return results;
        }

        public async Task InitAsync()
        {
            this.OrderStateMachine = new OrderStateMachine(new SpeechSubsystem(this), new Logger(this), new OrdersService(this));
            await this.OrderStateMachine.Initialize();
        }

        public async Task ProcessManualInput(String text)
        {
            this.Log.LogEvent($"Manual input: '{text}'");
            await this.OrderStateMachine.ProcessResponseAsync(text);
        }

        private class SpeechSubsystem : ISpeechSubsystem
        {
            private readonly MainViewModel mainViewModel;

            public SpeechSubsystem(MainViewModel mainViewModel)
            {
                this.mainViewModel = mainViewModel;
            }

            public Task SpeakAsync(String text)
            {
                return this.mainViewModel.SpeakAsync(text);
            }

            public void SetRecognitionMode(String mode)
            {
                this.mainViewModel.speechTopic = mode;
            }

            public void StopCommunication()
            {
            }
        }

        private class Logger : ILogger
        {
            private readonly MainViewModel mainViewModel;

            public Logger(MainViewModel mainViewModel)
            {
                this.mainViewModel = mainViewModel;
            }

            public void LogEvent(String eventText)
            {
                this.mainViewModel.Log.LogEvent(eventText);
            }
        }

        private class OrdersService : IOrdersService
        {
            private readonly MainViewModel mainViewModel;

            public OrdersService(MainViewModel mainViewModel)
            {
                this.mainViewModel = mainViewModel;
            }

            public Task CreateOrderAsync(OrderInfo order)
            {
                this.mainViewModel.window.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(this.mainViewModel.window, $@"Откуда: {order.AddressFrom}
Куда: {order.AddressTo}
Дата и время: {order.DateTime}
Телефон: {order.Phone}
Дополнительные пожелания: {order.AdditionalInfo}");
                });

                return Task.FromResult(0);
            }
        }
    }
}