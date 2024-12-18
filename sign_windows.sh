#!/bin/bash

AWS_USE_FIPS_ENDPOINT=true
jsign --storetype AWS --keystore us-east-1 --alias "FUTO-EV-signing-key" --tsaurl http://timestamp.globalsign.com/tsa/r6advanced1 --tsmode RFC3161 --certfile /c/cert/fullchain.pem $1