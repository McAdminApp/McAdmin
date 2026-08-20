pipeline {
    agent none

    environment {
        IMAGE_NAME = 'mcservermgmnt'
        APP_PORT   = '5700'
    }

    options {
        buildDiscarder(logRotator(numToKeepStr: '10'))
        timeout(time: 30, unit: 'MINUTES')
        disableConcurrentBuilds()
    }

    stages {

        stage('Checkout') {
            agent { label 'deb-slave01' }
            steps {
                checkout scm
            }
        }

        stage('Bygg image') {
            agent { label 'deb-slave01' }
            steps {
                // Kontexten är repots rot, inte src/web — webbprojektet refererar
                // plugin-projektet och båda måste ligga innanför kontexten.
                sh '''
                    docker build -f Dockerfile \
                        -t ${IMAGE_NAME}:${BUILD_NUMBER} \
                        -t ${IMAGE_NAME}:latest .
                '''
            }
        }

        stage('Paketera plugin-API') {
            agent { label 'deb-slave01' }
            steps {
                // Plugin-API:t packas i samma image-bygge och exporteras som .nupkg,
                // så Jenkins-noden inte behöver något .NET SDK installerat. Lagren
                // från föregående stage är redan cachade, så det här går fort.
                sh '''
                    rm -rf artifacts
                    DOCKER_BUILDKIT=1 docker build -f Dockerfile \
                        --target package \
                        --output type=local,dest=artifacts .
                '''
                archiveArtifacts artifacts: 'artifacts/*.nupkg', fingerprint: true, allowEmptyArchive: false
            }
        }

        stage('Deploy') {
            agent { label 'deb-slave01' }
            steps {
                // RCON-losenordet ligger i Jenkins credentials och skickas in till
                // docker compose, som satter det som McServer__RconPassword i containern.
                withCredentials([
                    string(credentialsId: 'mcservermgmnt-rcon-password', variable: 'MC_RCON_PASSWORD')
                ]) {
                    // docker-compose.yml ligger kvar i repots rot. Compose härleder
                    // projektnamnet ur katalogen filen står i, och projektnamnet är det
                    // som prefixar volymerna mcservermgmnt_data och _keys. Flyttas filen
                    // ner i src/web tappar deployen både databasen och auth-nycklarna.
                    sh 'docker compose up -d --force-recreate'
                }
            }
        }

        stage('Hälsokontroll') {
            agent { label 'deb-slave01' }
            steps {
                retry(5) {
                    sleep(time: 5, unit: 'SECONDS')
                    sh 'curl -sf --max-time 10 http://localhost:${APP_PORT}/login > /dev/null'
                }
                echo "Hälsokontroll OK – applikationen svarar på http://localhost:${APP_PORT}"
            }
        }

        stage('Städa gamla images') {
            agent { label 'deb-slave01' }
            steps {
                // Tar bort dinglande lager från tidigare byggen, inte de taggade imagerna.
                sh 'docker image prune -f || true'
            }
        }
    }

    post {
        success {
            echo "✓ Build #${BUILD_NUMBER} lyckades och är driftsatt."
        }
        failure {
            echo "✗ Build #${BUILD_NUMBER} misslyckades."
        }
    }
}
