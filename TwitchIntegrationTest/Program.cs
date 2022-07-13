using IdentityModel.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Web;

//TODO add a logger

//Dependency Injection
ServiceCollection serviceCollection = new ServiceCollection();
IConfigurationRoot configuration;

//To be filled with the token when its received
string OAuthBearerToken = "";

// Build configuration
configuration = new ConfigurationBuilder()
	.SetBasePath(Directory.GetParent(AppContext.BaseDirectory)?.FullName)
	.AddJsonFile("appsettings.json", false)
    //I don't know if you're familiar with user secrets yet, so if the application says "400, missing client ID"
    //What you need to do is right-click the .csproj, find manage user secrets.
    //This opens an empty JSON file, copy the contents of "secrets.json" into it and fill in your clientID there.
    .AddUserSecrets("38721d3a-b198-49f1-828a-06b8b163c6d1")
	.Build();

// Add access to generic IConfigurationRoot
serviceCollection.AddSingleton<IConfigurationRoot>(configuration);
Console.WriteLine($"Loading settings complete - Client ID: {configuration.GetSection("TwitchSettings")["clientId"]}");

//Set up OAuth response handler
Console.WriteLine("Setting up OAuth response server");
HttpListener jsTokenLifterListener = new HttpListener();
jsTokenLifterListener.Prefixes.Add(configuration.GetSection("replyHandler")["jsHostUrl"]);
jsTokenLifterListener.Start();

//Suppress warning for cheap Fire&Forget single use webserver
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
//TODO move this 
jsTokenLifterListener.GetContextAsync().ContinueWith(x =>
{
	global::System.Console.WriteLine($"I have possibly received an OAuth reply!");
    global::System.Console.WriteLine($"It's going to... \n {x.Result.Request.Url}");
    using StreamReader a = new StreamReader(x.Result.Request.InputStream);
	string body = a.ReadToEnd();
    global::System.Console.WriteLine($"And says... \n {body}");
    global::System.Console.WriteLine($"And the headers are... {x.Result.Request.Headers}");
	x.Result.Response.StatusCode = (int) HttpStatusCode.OK;
    //Empty webpage with JS that will:
    //Receive the token in Hash parameters
    //Parse it
    //Call this application again on http://localhost:30001/ with the token in the query params
    var simpleHtmlReply = @"<html></html> <script> var hash = document.location.hash.substring(1);var params = {};hash.split('&').map(hk => { let temp = hk.split('='); params[temp[0]] = temp[1]}); window.location = 'http://localhost:30001?token='+params['access_token']; </script>";
    using StreamWriter b = new StreamWriter(x.Result.Response.OutputStream);
    b.WriteLine(simpleHtmlReply);
});
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

/**
 * This is a bad insecure hack
 * If Twitch finds our we're doing this, we'll get the application banned for sure
 * Allow me to explain why we need to do this in the first place:
 * After we do the request for an OAuth token to Twitch we expect them to pass the response on to the application
 * The way they do this, is through |hash parameters| https://www.oho.com/blog/explained-60-seconds-hash-symbols-urls-and-seo
 * This in itself is a hack using HTML functions, where the parameter is not passed to the server
 * so because Twitch redirects to https://mywebsite/#access_token=your_super_secret_token it never passes the "access_token=your_super_secret_token" part
 * but browsers will be able to read it! This makes it great for secret information between javascript applications
 * Now enter our C# applicaiton, which despite running locally on the User's PC it sadly is still seen as 'remote' to the browser application
 * The result being that parameters after the # are not transmitted to our application and there is no way to do so
 * UNLESS!! We have the browser open a nasty webpage with javascript code that reads the token from the hash parameters and then... 
 * Sends it to our application albeit very insecurely on a second HTTP listener.
 * An example (easy) attack to steal a User's token would involve a third malicious person making an application that listens on exactly the same port as ours
 * which isn't hard to guess, it's 30001, and then have that application intercept the token and it has the freedom to do as it wants with the User's account. 
*/

Console.WriteLine("Setting up OAuth Token Receiving server");
HttpListener tokenReceiverListener = new HttpListener();
tokenReceiverListener.Prefixes.Add(configuration.GetSection("replyHandler")["gameHostUrl"]);
tokenReceiverListener.Start();

//Suppress warning for cheap Fire&Forget single use webserver
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
//TODO move this
tokenReceiverListener.GetContextAsync().ContinueWith(x =>
{
    OAuthBearerToken = x.Result.Request.QueryString["token"] ?? "";
    global::System.Console.WriteLine($"We've stolen a token!");
    global::System.Console.WriteLine($"Extracted {OAuthBearerToken}");
    x.Result.Response.StatusCode = (int)HttpStatusCode.OK;
    //Show User this call succeeded in Browser
    var simpleHtmlReply = @"<html><h1>You're all set to use this application!</h1><div style=""color: green;"">Token received by game manager!</div> You may now close this tab. </html>";
    using StreamWriter b = new StreamWriter(x.Result.Response.OutputStream);
    b.WriteLine(simpleHtmlReply);
    global::System.Console.WriteLine("You may now hit enter to test the token & validate it :)");
});
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
Console.WriteLine($"Listening on {configuration.GetSection("replyHandler")["gameHostUrl"]}...");

Console.WriteLine("Setting up OAuth request");
//Build the URI to the Twitch API, set the Query parameters with our settings from Application we registered on Twitch's dev console
var uriBuilder = new UriBuilder("https://id.twitch.tv/oauth2/authorize");
var query = HttpUtility.ParseQueryString(uriBuilder.Query);
query["client_id"] = configuration.GetSection("TwitchSettings")["clientId"];
query["force_verify"] = true.ToString();
query["redirect_uri"] = configuration.GetSection("replyHandler")["jsHostUrl"];
query["response_type"] = "token";
query["scope"] = ""; //TODO figure out necessary scopes
query["state"] = "registering"; 
uriBuilder.Query = query.ToString();

Console.WriteLine($"Sending query to {uriBuilder}");

//Open the User's web browser to the Twitch authentication page and have them log in. Once they do, we will asynchronously receive the response on the web server started earlier
try
{
    Console.WriteLine("Attempting request for OAuth token");
    Process.Start(uriBuilder.ToString());
}
catch
{
    // hack because of this: https://github.com/dotnet/corefx/issues/10361
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        Console.WriteLine("Reattempting request with Windows hack");
        Process.Start(new ProcessStartInfo(uriBuilder.ToString()) { UseShellExecute = true });
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        Console.WriteLine("Reattempting request with Linux hack");
        Process.Start("xdg-open", uriBuilder.ToString());
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        Console.WriteLine("Reattempting request with MacOS hack");
        Process.Start("open", uriBuilder.ToString());
    }
    else
    {
        Console.WriteLine("Failed, crashing gracefully");
        throw;
    }
}

Console.WriteLine("Wait until the authentication is complete, hit enter to test token with validator");
Console.ReadLine();

Console.WriteLine("Attempting validation...");
//Verify our gathered OAuth token works by passing it to the validation API of Twitch (I think this is part of "Helix")
var validatorUriBuilder = new UriBuilder("https://id.twitch.tv/oauth2/validate");
HttpClient client = new HttpClient();
client.SetBearerToken(OAuthBearerToken); //C# is the best, easy to pass the token along
var response = await client.GetAsync(validatorUriBuilder.ToString());
var body = await response.Content.ReadAsStringAsync();
Console.WriteLine($"Validation response: {response.StatusCode} | {body}");
