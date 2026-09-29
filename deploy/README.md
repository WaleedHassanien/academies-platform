# Deploying to a free VM (Oracle Cloud Always Free)

On every push to `main`, `.github/workflows/docker.yml` does two things:
1. **Build:** it builds the 7 images for amd64 and arm64 and pushes them to GHCR.
2. **Deploy:** it SSHes into the server, pulls the new images and restarts the stack
   (`docker-compose.prod.yml`: MySQL, Redis, RabbitMQ, 5 APIs, gateway, client and Caddy for HTTPS).

The deploy step is skipped until the `DEPLOY_HOST` secret exists, so the setup below can be done at any time.

The whole stack needs about 3–4 GB of RAM. Oracle's Always Free **Ampere A1** shape (up to 4 OCPU / 24 GB,
free forever) is enough. Any other Linux VM with Docker works the same way.

## 1. Create the VM

1. Sign up at <https://www.oracle.com/cloud/free/>. A card is required for verification; Always Free
   resources are not charged.
2. **Compute → Instances → Create instance:**
   - Image: **Ubuntu 24.04**.
   - Shape: **Ampere → VM.Standard.A1.Flex**, 2–4 OCPU and 12–24 GB RAM.
   - Add your SSH public key.
   - If the region reports "out of capacity", retry later or pick fewer OCPUs.
3. Note the instance's **public IP**.
4. Open ports 80 and 443 in the cloud firewall:
   - Go to **Networking → Virtual cloud networks → (your VCN) → Security Lists → Default**.
   - Add two ingress rules: source `0.0.0.0/0`, TCP ports **80** and **443**.

## 2. Prepare the server

```bash
ssh ubuntu@<PUBLIC_IP>

# Docker
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER

# Oracle's Ubuntu image also blocks ports in iptables
sudo iptables -I INPUT 6 -p tcp -m multiport --dports 80,443 -j ACCEPT
sudo netfilter-persistent save

exit   # log out and back in so the docker group applies
```

## 3. Get a domain name

HTTPS needs a host name that points at the IP. Pick one:

- **DuckDNS (free):** sign in at <https://www.duckdns.org>, create `yourname.duckdns.org` and point it at the public IP.
- **No sign-up:** use `<ip-with-dashes>.sslip.io`, for example `129-146-10-20.sslip.io`.
- **Your own domain:** add an `A` record pointing at the IP.

## 4. Create the server's `.env` (once)

```bash
mkdir -p ~/academies && cd ~/academies
curl -fsSLO https://raw.githubusercontent.com/WaleedHassanien/academies-platform/main/deploy/.env.prod.example
cp .env.prod.example .env
nano .env   # set DOMAIN and replace every change-me (openssl rand -hex 24)
```

If the repository is private, the `curl` fails. Copy `deploy/.env.prod.example` over with `scp` instead.

## 5. Connect GitHub

1. Create an SSH key only for deployments. On your PC:
   ```bash
   ssh-keygen -t ed25519 -f academies_deploy -N ""
   ```
2. Append `academies_deploy.pub` to `~/.ssh/authorized_keys` on the server.
3. In GitHub, open **Settings → Secrets and variables → Actions → New repository secret** and add:

| Secret | Value |
|---|---|
| `DEPLOY_HOST` | public IP of the VM |
| `DEPLOY_USER` | `ubuntu` |
| `DEPLOY_SSH_KEY` | full contents of the private key file `academies_deploy` |

Then push to `main`, or run **Actions → Docker images & deploy → Run workflow**.

- **First start:** each service applies its migrations, and Identity creates the SuperAdmin from
  `SEED_SUPERADMIN_EMAIL` / `SEED_SUPERADMIN_PASSWORD`.
- **Signing key:** `deploy.sh` generates the JWT signing key once, in `~/academies/keys/signing.pem`. Keep that file.
- **Open the app** at `https://<DOMAIN>`.

## Operating

```bash
cd ~/academies
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs -f identity-api
bash deploy.sh                      # redeploy the tags in .env / the environment
IMAGE_TAG=<older-sha> bash deploy.sh  # roll back
```

- **Private repository:** the GHCR images are private too. The workflow logs the server in with its own
  token for each deploy. For manual pulls, run `docker login ghcr.io` with a personal access token that
  has `read:packages`.
- **Backups:** `docker compose -f docker-compose.prod.yml exec mysql mysqldump -uroot -p"$MYSQL_ROOT_PASSWORD" academies > backup.sql`
  (run `source .env` first).
