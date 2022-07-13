# TwitchOauthTest
A test application to attempt usage of the Twitch API

Because the OAuth implementation by default and for security reasons doesn't allow a standalone C# app use tokens, we had to get a bit creative. 
This is a proof of concept showing a possible way of getting the token from the Twitch API regardless and use it to make API calls with a C# application that can be distributed.
