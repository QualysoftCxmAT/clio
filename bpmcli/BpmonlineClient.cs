﻿using System;
using System.IO;
using System.Net;
using System.Text;

namespace bpmcli
{
	public class BpmonlineClient
	{

		#region Fields: private

		private string _appUrl;

		private string _userName;

		private string _userPassword;

		private string LoginUrl => _appUrl + @"/ServiceModel/AuthService.svc/Login";

		private string PingUrl => _appUrl + @"/0/ping";

		private CookieContainer AuthCookie = new CookieContainer();

		#endregion

		#region Methods: Public

		public BpmonlineClient(string appUrl, string userName, string userPassword) {
			_appUrl = appUrl;
			_userName = userName;
			_userPassword = userPassword;
		}

		public void Login() {
			var authRequest = HttpWebRequest.Create(LoginUrl) as HttpWebRequest;
			authRequest.Method = "POST";
			authRequest.ContentType = "application/json";
			authRequest.CookieContainer = AuthCookie;
			using (var requestStream = authRequest.GetRequestStream()) {
				using (var writer = new StreamWriter(requestStream)) {
					writer.Write(@"{
						""UserName"":""" + _userName + @""",
						""UserPassword"":""" + _userPassword + @"""
					}");
				}
			}
			using (var response = (HttpWebResponse)authRequest.GetResponse()) {
				if (response.StatusCode == HttpStatusCode.OK) {
					using (var reader = new StreamReader(response.GetResponseStream())) {
						var responseMessage = reader.ReadToEnd();
						if (responseMessage.Contains("\"Code\":1")) {
							throw new UnauthorizedAccessException($"Unauthotized {_userName} for {_appUrl}");
						}
					}
					string authName = ".ASPXAUTH";
					string headerCookies = response.Headers["Set-Cookie"];
					string authCookeValue = GetCookieValueByName(headerCookies, authName);
					AuthCookie.Add(new Uri(_appUrl), new Cookie(authName, authCookeValue));
				}
			}
		}

		public string ExecuteGetRequest(string url) {
			HttpWebRequest request = CreateRequest(url);
			request.Timeout = 100000;
			request.Method = "GET";
			string responseFromServer;
			using (WebResponse response = request.GetResponse()) {
				using (var dataStream = response.GetResponseStream()) {
					using (StreamReader reader = new StreamReader(dataStream)) {
						responseFromServer = reader.ReadToEnd();
					}
				}
			}
			return responseFromServer;
		}

		public string ExecutePostRequest(string url, string requestData) {
			HttpWebRequest request = CreateRequest(url);
			using (var requestStream = request.GetRequestStream()) {
				using (var writer = new StreamWriter(requestStream)) {
					writer.Write($"{requestData}");
				}
			}
			string responseFromServer = string.Empty;
			using (WebResponse response = request.GetResponse()) {
				using (var dataStream = response.GetResponseStream()) {
					using (StreamReader reader = new StreamReader(dataStream)) {
						responseFromServer = reader.ReadToEnd();
					}
				}
			}
			return responseFromServer;
		}

		public string UploadFile(string url, string filePath) {
			FileInfo fileInfo = new FileInfo(filePath);
			string fileName = fileInfo.Name;
			string boundary = DateTime.Now.Ticks.ToString("x");
			HttpWebRequest request = CreateRequest(url);
			request.ContentType = "multipart/form-data; boundary=" + boundary;
			Stream memStream = new MemoryStream();
			var boundarybytes = Encoding.ASCII.GetBytes("\r\n--" + boundary + "\r\n");
			var endBoundaryBytes = Encoding.ASCII.GetBytes("\r\n--" + boundary + "--");
			string headerTemplate =
				"Content-Disposition: form-data; name=\"{0}\"; filename=\"{1}\"\r\n" +
				"Content-Type: application/octet-stream\r\n\r\n";
			memStream.Write(boundarybytes, 0, boundarybytes.Length);
			var header = string.Format(headerTemplate, "files", fileName);
			var headerbytes = Encoding.UTF8.GetBytes(header);
			memStream.Write(headerbytes, 0, headerbytes.Length);
			using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read)) {
				var buffer = new byte[1024];
				var bytesRead = 0;
				while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) != 0) {
					memStream.Write(buffer, 0, bytesRead);
				}
			}
			memStream.Write(endBoundaryBytes, 0, endBoundaryBytes.Length);
			request.ContentLength = memStream.Length;
			using (Stream requestStream = request.GetRequestStream()) {
				memStream.Position = 0;
				byte[] tempBuffer = new byte[memStream.Length];
				memStream.Read(tempBuffer, 0, tempBuffer.Length);
				memStream.Close();
				requestStream.Write(tempBuffer, 0, tempBuffer.Length);
			}
			string responseFromServer;
			using (WebResponse response = request.GetResponse()) {
				Console.WriteLine(((HttpWebResponse)response).StatusDescription);
				using (var dataStream = response.GetResponseStream()) {
					using (StreamReader reader = new StreamReader(dataStream)) {
						responseFromServer = reader.ReadToEnd();
						}
				}
			}
			return responseFromServer;
		}

		public void DownloadFile(string url, string filePath, string requestData) {
			HttpWebRequest request = CreateRequest(url);
			using (var requestStream = request.GetRequestStream()) {
				using (var writer = new StreamWriter(requestStream)) {
					writer.Write(requestData);
				}
			}
			using (WebResponse response = request.GetResponse()) {
				using (var dataStream = response.GetResponseStream()) {
					if (dataStream != null) {
						using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write)) {
							dataStream.CopyTo(fileStream);
						}
					}
				}
			}
		}

		#endregion

		#region Methods: private

		private void InitConnection() {
			if (AuthCookie.Count == 0) {
				Login();
				PingApp();
			}
		}

		private void AddCsrfToken(HttpWebRequest request) {
			var bpmcsrf = request.CookieContainer.GetCookies(new Uri(_appUrl))["BPMCSRF"];
			if (bpmcsrf != null) {
				request.Headers.Add("BPMCSRF", bpmcsrf.Value);
			}
		}

		private string GetCookieValueByName(string headerCookies, string name) {
			string tokens = headerCookies.Replace("HttpOnly,", string.Empty);
			string[] cookies = tokens.Split(';');
			foreach (var cookie in cookies) {
				if (cookie.Contains(name)) {
					return cookie.Split('=')[1];
				}
			}
			return string.Empty;
		}

		private void PingApp() {
			var pingRequest = CreateRequest(PingUrl);
			pingRequest.Timeout = 60000;
			using (var requestStream = pingRequest.GetRequestStream()) {
				using (var writer = new StreamWriter(requestStream)) {
					writer.Write(@"{}");
				}
			}
			try {
				using (var response = (HttpWebResponse)pingRequest.GetResponse()) {
				}
			} catch (Exception e) {
				Console.WriteLine(e);
				throw;
			}
		}

		private HttpWebRequest CreateRequest(string url) {
			InitConnection();
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			request.ContentType = "application/json";
			request.Method = "POST";
			request.KeepAlive = true;
			request.CookieContainer = AuthCookie;
			AddCsrfToken(request);
			return request;
		}

		#endregion

	}
}