#!/bin/bash
git pull
docker build -t tiny-city-card-game .
docker rm -f tiny-city-container || true
docker run -d -p 5040:5040 --name tiny-city-container --restart always -v $(pwd)/Cards.xlsx:/app/Cards.xlsx tiny-city-card-game
docker ps
