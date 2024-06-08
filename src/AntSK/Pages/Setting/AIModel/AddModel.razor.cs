﻿using AntDesign;
using AntDesign.ProLayout;
using AntSK.Domain.Domain.Interface;
using AntSK.Domain.Domain.Model.Constant;
using AntSK.Domain.Domain.Model.Enum;
using AntSK.Domain.Domain.Other.Bge;
using AntSK.Domain.Domain.Service;
using AntSK.Domain.Options;
using AntSK.Domain.Repositories;
using AntSK.Domain.Utils;
using AntSK.LLamaFactory.Model;
using BlazorComponents.Terminal;
using DocumentFormat.OpenXml.Office2010.Excel;
using Downloader;
using Microsoft.AspNetCore.Components;
using NRedisStack.Search;
using System.ComponentModel;

namespace AntSK.Pages.Setting.AIModel
{
    public partial class AddModel
    {
        [Parameter]
        public string ModelId { get; set; }
        [Parameter]
        public string ModelPath { get; set; }
        [Inject] protected IAIModels_Repositories _aimodels_Repositories { get; set; }
        [Inject] protected MessageService? Message { get; set; }
        [Inject] public HttpClient HttpClient { get; set; }

        [Inject] protected ILLamaFactoryService _ILLamaFactoryService { get; set; }
        [Inject] protected IDics_Repositories _IDics_Repositories { get; set; }

        [Inject] IConfirmService _confirmService { get; set; }

        private AIModels _aiModel = new AIModels();

        //llamasharp download
        private string _downloadUrl;
        private bool _downloadModalVisible;
        private bool _isComplete;
        private double _downloadProgress;
        private bool _downloadFinished;
        private bool _downloadStarted;
        private IDownload _download;

        private Modal _modal;
        private string[] _modelFiles;

        //menu
        private IEnumerable<string> _menuKeys;
        private List<MenuDataItem> menuList = new List<MenuDataItem>();

        //llamafactory
        private List<LLamaModel> modelList=new List<LLamaModel>();
        private bool llamaFactoryIsStart = false;
        private Dics llamaFactoryDic= new Dics();
        //日志输出
        private  BlazorTerminal blazorTerminal = new BlazorTerminal();
        private TerminalParagraph para;
        private bool _logModalVisible;

        private List<string> bgeEmbeddingList = new List<string>() { "AI-ModelScope/bge-small-zh-v1.5", "AI-ModelScope/bge-base-zh-v1.5", "AI-ModelScope/bge-large-zh-v1.5" };
        private List<string> bgeRerankList = new List<string>() { "Xorbits/bge-reranker-base", "Xorbits/bge-reranker-large", "AI-ModelScope/bge-reranker-v2-m3", "AI-ModelScope/bge-reranker-v2-gemma"};
        private bool BgeEmbeddingIsStart = false;
        private string BgeEmbeddingBtnText = "初始化";

        private bool BgeRerankIsStart = false;
        private string BgeRerankBtnText = "初始化";


        protected override async Task OnInitializedAsync()
        {
            try
            {
                await base.OnInitializedAsync();
                if (!string.IsNullOrEmpty(ModelId))
                {
                    _aiModel = _aimodels_Repositories.GetFirst(p => p.Id == ModelId);
                }

                modelList = _ILLamaFactoryService.GetLLamaFactoryModels();
                llamaFactoryDic = await _IDics_Repositories.GetFirstAsync(p => p.Type == LLamaFactoryConstantcs.LLamaFactorDic && p.Key == LLamaFactoryConstantcs.IsStartKey);
                if (llamaFactoryDic != null)
                {
                    llamaFactoryIsStart = llamaFactoryDic.Value == "false" ? false : true;
                }

               
                //目前只支持gguf的 所以筛选一下
                _modelFiles = Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), FileDirOption.DirectoryPath)).Where(p=> p.Contains(".gguf")||p.Contains(".ckpt")|| p.Contains(".safetensors")).ToArray();
                if (!string.IsNullOrEmpty(ModelPath))
                {
                    string extension = Path.GetExtension(ModelPath);
                    switch (extension)
                    {
                        case ".gguf":
                            _aiModel.AIType = AIType.LLamaSharp;
                            break;
                        case ".safetensors":
                        case ".ckpt":
                            _aiModel.AIType = AIType.StableDiffusion;
                            break;

                    }
                    //下载页跳入
                 
                    _downloadModalVisible = true;

                    _downloadUrl = $"https://hf-mirror.com{ModelPath.Replace("---","/")}";
                }        
            }
            catch 
            {
                _ = Message.Error("LLamaSharp.FileDirectory目录配置不正确！", 2);
            }
        }

        private void HandleSubmit()
        {
            if (_aimodels_Repositories.IsAny(p => p.Id!=_aiModel.Id.ConvertToString()&& p.AIModelType == _aiModel.AIModelType && p.EndPoint == _aiModel.EndPoint.ConvertToString() && p.ModelKey == _aiModel.ModelKey && p.ModelName == _aiModel.ModelName))
            {
                _ = Message.Error("模型已存在！", 2);
                return;
            }
            if (_aiModel.AIType.IsNull())
            {
                _ = Message.Error("AI类型必须选择", 2);
                return;
            }
            if (_aiModel.AIModelType.IsNull())
            {
                _ = Message.Error("模型类型必须选择", 2);
                return;
            }
            if (string.IsNullOrEmpty(ModelId))
            {
                //新增
                _aiModel.Id = Guid.NewGuid().ToString();

                if (_aimodels_Repositories.IsAny(p => p.ModelDescription == _aiModel.ModelDescription))
                {
                    _ = Message.Error("模型描述已存在！", 2);
                    return;
                }
                _aimodels_Repositories.Insert(_aiModel);
            }
            else
            {
                _aimodels_Repositories.Update(_aiModel);
            }

            Back();
        }

        private void Back()
        {
            NavigationManager.NavigateTo("/modelmanager/modellist");
        }

        private async Task StartDownload()
        {
            if (string.IsNullOrWhiteSpace(_downloadUrl))
            {
                return;
            }

            _download = DownloadBuilder.New()
            .WithUrl(_downloadUrl)
            .WithDirectory(Path.Combine(Directory.GetCurrentDirectory(), FileDirOption.DirectoryPath))
            .WithConfiguration(new DownloadConfiguration()
            {
                ParallelCount = 5,
            })
            .Build();

            _download.DownloadProgressChanged += DownloadProgressChanged;
            _download.DownloadFileCompleted += DownloadFileCompleted;
            _download.DownloadStarted += StartedDownload;

            await _download.StartAsync();

            //download.Stop(); // cancel current download
        }

        private void DownloadProgressChanged(object? sender, DownloadProgressChangedEventArgs e)
        {
            _downloadProgress = Math.Round( e.ProgressPercentage,2);
            InvokeAsync(StateHasChanged);
        }

        private void DownloadFileCompleted(object? sender, AsyncCompletedEventArgs e)
        {
            _downloadFinished = true;
            _aiModel.ModelName = _download.Package.FileName;
            _downloadModalVisible = false;
            _downloadStarted = false;
            _modelFiles = Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), FileDirOption.DirectoryPath));
            InvokeAsync(StateHasChanged);
        }

        private void StartedDownload(object? sender, DownloadStartedEventArgs e)
        {
            _downloadStarted = true;
            InvokeAsync(StateHasChanged);
        }

        private void OnCancelDownload()
        {
            if (_downloadStarted)
            {
                return;
            }

            _downloadModalVisible = false;
        }

        private void StopDownload()
        {
            _downloadStarted=false;
            _download?.Stop();
            InvokeAsync(StateHasChanged);
        }

        private void OnSearch(string value)
        {
            if (string.IsNullOrEmpty(value))
            { 
                modelList = _ILLamaFactoryService.GetLLamaFactoryModels(); 
            }
            else
            {
                modelList = _ILLamaFactoryService.GetLLamaFactoryModels().Where(p => p.Name.ToLower().Contains(value.ToLower())).ToList();
            }

        }
      
        /// <summary>
        /// 启动服务
        /// </summary>
        private async Task StartLFService()
        {
            if (string.IsNullOrEmpty(_aiModel.ModelName))
            {
                _ = Message.Error("请先选择模型！", 2);
                return;
            }
            llamaFactoryIsStart = true;
            _logModalVisible = true;
            llamaFactoryDic.Value = "true";
            _IDics_Repositories.Update(llamaFactoryDic);
            _ILLamaFactoryService.LogMessageReceived -= CmdLogHandler;
            _ILLamaFactoryService.LogMessageReceived += CmdLogHandler;
            _ILLamaFactoryService.StartLLamaFactory(_aiModel.ModelName, "default");
        }

        private void StopLFService()
        {
            llamaFactoryIsStart = false;
            llamaFactoryDic.Value = "false";
            _IDics_Repositories.Update(llamaFactoryDic);
            _ILLamaFactoryService.KillProcess();
        }
        private async Task PipInstall()
        {
            var content = "初次使用需要执行pip install，点击确认后可自动执行，是否执行";
            var title = "提示";
            var result = await _confirmService.Show(content, title, ConfirmButtons.YesNo);
            if (result == ConfirmResult.Yes)
            {
                _logModalVisible = true;
                _ILLamaFactoryService.LogMessageReceived -= CmdLogHandler;
                _ILLamaFactoryService.LogMessageReceived += CmdLogHandler;
                _ILLamaFactoryService.PipInstall();
            }
        }

        private async Task BgeEmbedding()
        {
            if (string.IsNullOrEmpty(_aiModel.ModelName))
            {
                _ = Message.Error("请输入模型名称！", 2);
                return;
            }
            if (string.IsNullOrEmpty(_aiModel.EndPoint))
            {
                _ = Message.Error("请输入正确的Python dll或python so路径！", 2);
                return;
            }

            BgeEmbeddingIsStart = true;
            BgeEmbeddingBtnText = "正在初始化...";
            await Task.Run(() =>
            {
                try
                {
                    BgeEmbeddingConfig.LoadModel(_aiModel.EndPoint, _aiModel.ModelName);
                    BgeEmbeddingBtnText = "初始化完成";
                    BgeEmbeddingIsStart = false;
                }
                catch (System.Exception ex)
                {
                    _ = Message.Error(ex.Message, 2);
                    BgeEmbeddingIsStart = false;
                }
            });   
        }

        private async Task BgeRerank()
        {
            if (string.IsNullOrEmpty(_aiModel.ModelName))
            {
                _ = Message.Error("请输入模型名称！", 2);
                return;
            }
            if (string.IsNullOrEmpty(_aiModel.EndPoint))
            {
                _ = Message.Error("请输入正确的Python dll或python so路径！", 2);
                return;
            }

            BgeRerankIsStart = true;
            BgeRerankBtnText = "正在初始化...";
            await Task.Run(() =>
            {
                try
                {
                    BegRerankConfig.LoadModel(_aiModel.EndPoint, _aiModel.ModelName);
                    BgeRerankBtnText = "初始化完成";
                    BgeRerankIsStart = false;
                }
                catch (System.Exception ex)
                {
                    _ = Message.Error(ex.Message, 2);
                    BgeRerankIsStart = false;
                }
            });
        }


        private async Task CmdLogHandler(string message)
        {
            await InvokeAsync(() =>
            {
                para = blazorTerminal.RespondText("");
                para.AddTextLine(message);          
            });
        }
        /// <summary>
        /// 停止服务
        /// </summary>
   
        private void OnCancelLog() {
            _logModalVisible = false;
        }

        private void AITypeChange(AIType aiType) 
        {
            switch (aiType)
            { 
                case AIType.LLamaFactory:
                    _aiModel.EndPoint = "http://localhost:8000/";
                    _aiModel.AIModelType=AIModelType.Chat;
                    break;
                case AIType.StableDiffusion:
                    _aiModel.AIModelType = AIModelType.Image;
                    break;
                case AIType.Mock:
                    _aiModel.AIModelType = AIModelType.Chat;
                    break ;
                case AIType.BgeEmbedding:
                    _aiModel.AIModelType = AIModelType.Embedding;
                    break;
                case AIType.BgeRerank:
                    _aiModel.AIModelType = AIModelType.Rerank;
                    break;
                default:
                    _aiModel.AIModelType = AIModelType.Chat;
                    break;
            }
        }
    }
}
