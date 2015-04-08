/* Copyright (c) 2015 ETH Zurich
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * Kenny Mitchell
 */

// Are we using the TwoBigEars 3DCeption library?
//#define USE_TBE 1

// Are we using the POI-Interface enabler?
//define USE_POI 1

using UnityEngine;
using System;
using System.Collections;

#if USE_TBE
using TBE_3DCore;
#endif

#if USE_POI
using PoI.Data;
#else
public class Location
{
	public Location (double lat, double lon) { Latitude=lat; Longitude=lon; }
	public double Latitude { get; set; }
	public double Longitude  { get; set; }
	public static double Distance (Location p1, Location p2)
	{
		double R = 6371.0; // earth mean radius
		double t1 = Degree2Radian (p1.Latitude);
		double t2 = Degree2Radian (p2.Latitude);
		double dt = Degree2Radian (p2.Latitude - p1.Latitude);
		double ds = Degree2Radian (p2.Longitude - p1.Longitude);
		double a = Math.Sin (dt / 2.0) * Math.Sin (dt / 2.0) + Math.Cos (t1) * Math.Cos (t2) * Math.Sin (ds / 2.0) * Math.Sin (ds / 2.0);
		double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
		return R * c;
	}
	private static double Degree2Radian (double d) { return (d * Math.PI / 180.0); }	
	private static double Radian2Degree (double r) { return (r / Math.PI * 180.0); }
};
#endif


public class RealityMixerAugmentedAudio : MonoBehaviour
{
	private const int maxGnomes = 11;
	private const int maxVocals = 5;
	private int lastVocal = 0;

	private const double AUDIO_CULLING_DISTANCE = 300.0;
	private const float UTTER_GNOME_WAIT_BASE = 10.0f;
	private const float UTTER_GNOME_WAIT_VARIANCE = 6.0f;
	private const double AUDIO_DISTANCE_SCALE = 0.1;

	private float accumulator = 0.0f;
	private float waitTime = 5.0f;

#if USE_POI
	private PoiServer poiServer;
#endif

#if USE_TBE
	private TBE_Source theSource;
#else
	private AudioSource theSource;
#endif

	public AudioClip[] vocalArray;


	public void Start()
	{
#if USE_POI
		poiServer = this.GetComponent<PoiServer>();
#else
		Input.location.Start();
#endif

#if USE_TBE
		theSource = GetComponent<TBE_Source> ( ) ;
#else
		theSource = GetComponent<AudioSource>();
#endif

		Input.compass.enabled=true;
	}

	public int PersistentIdFromPoI(Location loc, int modulus)
	{
		// get stable Gnome's voice Id from nearest PoI

		// approximate based on limits of Haversine function (see Location class of POI enabler)
		double earthRadiusKM = 6378.137;
		double toMetres = earthRadiusKM * 2.0 * (double)Mathf.PI * 1000.0f;

		// Lat Long to a form of geohash (0-180range)

		UInt64 geohash = (UInt64)(toMetres*((loc.Latitude+90.0)*180.0+(loc.Longitude+90.0)));
		return (int)(geohash % (UInt64)modulus);
	}

	public double GPSBearing(double lat1, double lng1, double lat2, double lng2) 
	{
		lat1*=(double)Mathf.Deg2Rad;
		lng1*=(double)Mathf.Deg2Rad;
		lat2*=(double)Mathf.Deg2Rad;
		lng2*=(double)Mathf.Deg2Rad;
		// approximate spherical trig bearing
		double dLon = (lng1-lng2);
		double y = Math.Sin(dLon)*Math.Cos(lat1);
		double x = Math.Cos(lat2)*Math.Sin(lat1) - Math.Sin(lat2)*Math.Cos(lat1)*Math.Cos(dLon);
		double bearing = Math.Atan2(y, x);
		return bearing;
	}

	public double RelativeHeading(double hd1, double hd2)
	{
		double PI2=Math.PI*2.0;
		if (hd1>PI2 || hd1<0.0 || hd2>PI2 || hd2<0.0)
			return 0.0;
		
		double diff = hd2-hd1;
		double absDiff = Math.Abs(diff);
		
		if (absDiff<=Math.PI)
			return absDiff == Math.PI ? absDiff : diff;
		else if (hd2>hd1)
			return absDiff-PI2;

		return PI2-absDiff;
	}

	public Vector3 ProjectAudioLocation(double relativeHeading, double dist)
	{
		return new Vector3((float)(Math.Sin(relativeHeading)*dist),(float)(Math.Cos(relativeHeading)*dist),0.0f);
	}

	public void ProcessAudioSource()
	{
		// get nearest trader
		//Game.Instance.RequestTradingLocationUpdate();
#if USE_POI
		Location poiLocation = PoiServer.Instance.ClosestPoILocation;
#else 
		Location poiLocation=null;
#endif
		if (poiLocation==null)
		{
			poiLocation = new Location(55.64044952392576,-2.7828675270080558); // Scotland!
		}

		{
			int gnomeId = PersistentIdFromPoI(poiLocation, maxGnomes);
			int vocal = UnityEngine.Random.Range(0,maxVocals);
			if (vocal==lastVocal) vocal=(vocal+1) % maxVocals;

			theSource.clip=(vocalArray[gnomeId*maxVocals+vocal]);

			lastVocal=vocal;

			// get GPS location
#if USE_POI
			Location gpsLocation = GPS.Instance.PoILocation;
#else
			Location gpsLocation = new Location(Input.location.lastData.latitude,Input.location.lastData.longitude);
#endif

			// get audio distance
			double dist = AUDIO_DISTANCE_SCALE*Location.Distance(gpsLocation,poiLocation);
		
#if UNITY_EDITOR
			dist*=AUDIO_DISTANCE_SCALE;
#endif
			if (dist<AUDIO_CULLING_DISTANCE)
			{
				// get relative audio direction
				double bearing = GPSBearing(gpsLocation.Latitude, gpsLocation.Longitude, poiLocation.Latitude, poiLocation.Longitude);
				double deviceHeading = (double)(Mathf.Deg2Rad*Input.compass.trueHeading);

				double relativeHeading = RelativeHeading(deviceHeading,bearing);

				Vector3 audioPos = ProjectAudioLocation(relativeHeading,dist);
				theSource.transform.position=audioPos;

				Debug.Log(theSource.clip.name + " " + audioPos + " " + dist);
				theSource.PlayOneShot(theSource.clip);
			}
		}
	}

	public void Update()
	{
#if USE_POI
		if (!PoiServer.Instance.loggedIn) return;
#else
		if (Input.location.status == LocationServiceStatus.Initializing) return;
#endif

		accumulator += Time.deltaTime;
		if (accumulator >= waitTime)
		{
			// change enabled from true to false and vice-versa.
			ProcessAudioSource();

			waitTime = UTTER_GNOME_WAIT_BASE + UnityEngine.Random.Range(-UTTER_GNOME_WAIT_VARIANCE,UTTER_GNOME_WAIT_VARIANCE);
			accumulator = 0.0f;
		}
	}
}

