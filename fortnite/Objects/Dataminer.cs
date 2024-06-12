using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.VirtualFileSystem;
using CUE4Parse_Conversion.Textures;
using EpicManifestParser.Api;
using GenericReader;
using GTranslate.Translators;
using K4os.Compression.LZ4.Streams;
using Newtonsoft.Json;
using RestSharp;
using SkiaSharp;

using fortnite.Managers;
using fortnite.Objects.Graphics;

using Spectre.Console;
using fortnite.Objects;


namespace fortnite.Objects;

public class Dataminer
{
    public ESolitudeMode Mode { get; set; }
    private StreamedFileProvider _provider;
    private ChunkDownloader? _chunks;
    private string _backup;
    private List<VfsEntry>? _newFiles;
    private SKTypeface tf;

    public Dataminer(string mappingsPath, string backupPath)
    {
        _backup = backupPath;
        _provider = new(string.Empty, true, new VersionContainer(EGame.GAME_UE5_5));
        _provider.MappingsContainer = new FileUsmapTypeMappingsProvider(mappingsPath);
    }

    public async Task InstallDependenciesAsync(ManifestInfo manifestInfo)
    {
        _chunks = new ChunkDownloader();
        
        if (manifestInfo is null)
        {
            Log.Error("Manifest response content was empty.");
            return;
        }

        await _chunks.DownloadManifestAsync(manifestInfo);

        //_chunks.LoadFileForProvider("FortniteGame/Content/Paks/global.utoc", ref _provider);
        //_chunks.LoadFileForProvider("FortniteGame/Content/Paks/pakchunk10-WindowsClient.utoc", ref _provider); // hahahahahahahahahahahahahaha
    }
    public bool LocalResourcesDone { get; set; }
    public int LocalizedResourcesCount { get; set; }
    public async Task LoadFilesAsync()
    {
       await  _provider.MountAsync();
        
       // await _provider.MountAsync();
    }

    // would rather just support fmodel backups than make a seperate format
    public async Task LoadNewEntriesAsync() // https://github.com/4sval/FModel/blob/c014478abc4e455c7116504be92aa00eb00d757b/FModel/ViewModels/Commands/LoadCommand.cs#L144
    {
        var sw = Stopwatch.StartNew();

         _newFiles = new List<VfsEntry>();

       

        foreach (var asset in _provider.Files.Values)
        {
           // cancel if needed

            if (asset is not VfsEntry entry || entry.Path.EndsWith(".uexp") || entry.Path.EndsWith(".ubulk") || entry.Path.EndsWith(".uptnl"))
                continue;


            _newFiles.Add(entry);
            
        }





       

        sw.Stop();

        Log.Information("Found {Count} new files in {Milliseconds} ms", _newFiles.Count, sw.ElapsedMilliseconds);
    }

    public async Task DoYourThing()
    {
        Log.Information("Prepare for leaks");

        if (_newFiles is null)
        {
            Log.Error("New files are null");
            return;
        }

        var sw = Stopwatch.StartNew();

       
        await RunCosmeticsAsync();

       
    }

    private UTexture2D GetIconForCosmetic(UObject cosmetic, IEnumerable<VfsEntry>? offerImages)
    {
        if (cosmetic.ExportType == "AthenaPickaxeItemDefinition" &&
            cosmetic.TryGetValue(out FPackageIndex pickaxePtr, "WeaponDefinition") &&
            pickaxePtr.TryLoad(out var wid) &&
            wid is not null &&
            wid.TryGetValue(out UTexture2D pickaxeIcon, "LargePreviewImage"))
        {
            return pickaxeIcon;
        }

        if (cosmetic.TryGetValue<FSoftObjectPath>(out var displayAssetPtr, "DisplayAssetPath") &&
            displayAssetPtr.TryLoad(out var displayAsset) &&
            displayAsset.TryGetValue<FStructFallback>(out var tileImage, "TileImage") &&
            tileImage.TryGetValue<UTexture2D>(out var resourceObject, "ResourceObject"))
        {
            return resourceObject;
        }
        else if (cosmetic.TryGetValue(out FPackageIndex heroDefPtr, "HeroDefinition") &&
            heroDefPtr.TryLoad(out var heroDef) &&
            heroDef is not null &&
            heroDef.TryGetValue(out UTexture2D heroDefIcon, "LargePreviewImage"))
        {
            return heroDefIcon;
        }
        else if (cosmetic.TryGetValue(out UTexture2D cosmeticIcon, "LargePreviewImage"))
        {
            return cosmeticIcon;
        }

        return _provider.LoadObject<UTexture2D>("FortniteGame/Content/Athena/Prototype/Textures/T_Placeholder_Item_Outfit");
    }

    private static bool TryGetIconFromFile(UObject cosmetic, [NotNullWhen(true)] out SKBitmap? outIcon)
    {
        outIcon = null;

        if (cosmetic.ExportType != "AthenaCharacterItemDefinition")
            return false;

        var fileName = cosmetic.Name.Replace('_', '-');
        var iconFilePath = Path.Combine(DirectoryManager.OutfitsDir, $"T-AthenaSoldiers-{fileName}.png");

        if (!File.Exists(iconFilePath))
        {
            return false;
        }

        outIcon = SKBitmap.Decode(iconFilePath);

        return true;
    }

    public async Task RunCosmeticsAsync()
    {
        string dspname = "Asset";
        _chunks.LoadAllPaksForProvider(ref _provider); // download everything else because we got the quick stuff out 
        await LoadNewEntriesAsync();
       
        byte[] fontData = Properties.Resources.NotoSansArabic;
        using (MemoryStream fontStream = new MemoryStream(fontData))
        {
             tf = SKTypeface.FromStream(fontStream);
           
        }



        if (_newFiles is null)
            return;

        Log.Information("Creating merged cosmetics images");

        var sw = Stopwatch.StartNew();

        var imageInfo = new SKImageInfo(512, 562);
        var cosmeticIconInfo = new SKImageInfo(512, 512);
        var newCosmetics = _newFiles.Where(x => x.PathWithoutExtension.ToLower().StartsWith("fortnitegame/content/athena/items/cosmetics"));
        //var newCosmetics = _newFiles.Where(x => x.PathWithoutExtension.ToLower().StartsWith("fortnitegame/content/l10n/ar"));

        
        var offerImages = _newFiles.Where(x => x.PathWithoutExtension.StartsWith("FortniteGame/Content/Catalog/NewDisplayAssets"));

       

        if (newCosmetics.Count() == 0)
        {
            Log.Warning("No new cosmetics");
            return;
        }
        //var profile = new ProfileBuilder(newCosmetics.Count());
        int cosmeticIndex = 0;
       
        
        
        
        foreach (var cosmeticFile in newCosmetics)
        {
            if (!_provider.TryLoadObject(cosmeticFile.PathWithoutExtension, out var cosmetic))
                continue;

            using var icon = new FortniteIconCreator(imageInfo, tf);

            if (cosmetic.TryGetValue<UObject>(out var seriesPtr, "Series"))
            {
                icon.DrawRarityBackground(seriesPtr.Name, cosmeticIconInfo);
            }
            else if (cosmetic.TryGetValue<FName>(out var rarity, "Rarity"))
            {
                icon.DrawRarityBackground(rarity.Text, cosmeticIconInfo);
            }
            else
            {
                icon.DrawRarityBackground("Unattainable");
            }

            if (TryGetIconFromFile(cosmetic, out var cosmeticIcon))
            {
                icon.DrawAndResizeImage(cosmeticIcon, 0, 0, cosmeticIconInfo);
            }
            else
            {
                icon.DrawTexture(GetIconForCosmetic(cosmetic, offerImages), 0, 0, cosmeticIconInfo);
            }

            if (cosmetic.TryGetValue<FText>(out var displayName, "ItemName"))
            {
                dspname = displayName.Text;

                try
                {

                
                var translator = new MicrosoftTranslator();

                
                var result = await translator.TranslateAsync(displayName.Text.ToUpper(), "ar");
                icon.DrawDisplayName(result.Translation);
                }
                catch (Exception ex)
                {
                    var translator = new YandexTranslator();


                    var result = await translator.TranslateAsync(displayName.Text.ToUpper(), "ar");
                    icon.DrawDisplayName(result.Translation);

                }
            }

            var img = icon.GetImage();

            if (img is not null)
            {
                using var encoded = img.Encode(SKEncodedImageFormat.Png, 80);
                var filePath = Path.Join(DirectoryManager.OutputDir, $"{dspname}_{cosmeticIndex}.png");
                using var fs = File.Create(filePath);
                encoded?.AsStream().CopyTo(fs);
                cosmeticIndex++;
            }
        }

        //var allObjects = _provider.LoadAllObjects("FortniteGame/Content/Creative/Devices/Common/Localization/StringTable_Device_BetaPermissions.uasset"); // {GAME}/Content/Folder1/Folder2/PackageName.uasset
        //var fullJson = JsonConvert.SerializeObject(allObjects, Formatting.Indented);
        //File.WriteAllText(Path.Join(DirectoryManager.OutputDir, "athena.json"), fullJson);

        //File.WriteAllText(Path.Join(DirectoryManager.OutputDir, "profile_athena.json"), profile.Build());
        sw.Stop();

        Log.Information("Created merged image with {Num} cosmetics in {Time} ms", newCosmetics.Count(), sw.ElapsedMilliseconds);


    }

   

}
