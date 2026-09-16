# Builds the frontend into a container image. Like the backend Dockerfile,
# package.json is copied and `npm install` run BEFORE the rest of the source
# is copied in, so Docker only re-runs the (slow) install step when
# package.json actually changes.
FROM node:22-alpine

WORKDIR /app

COPY package.json ./
RUN npm install

COPY . .

EXPOSE 5173

# Runs Vite's dev server (with hot-reloading) rather than building a static
# production bundle - this project is set up for local development, not a
# production deployment.
CMD ["npm", "run", "dev"]
