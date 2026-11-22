{{/* Nom complet de la release, tronqué à 63 caractères (limite DNS). */}}
{{- define "souqly.fullname" -}}
{{- if contains .Chart.Name .Release.Name -}}
{{- .Release.Name | trunc 50 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name .Chart.Name | trunc 50 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{/* Étiquettes communes (l'instance vient avec souqly.selectorLabels ou souqly.instanceLabel). */}}
{{- define "souqly.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
app.kubernetes.io/part-of: souqly
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end -}}

{{/* Sélecteur d'un service : appelé avec (dict "root" $ "name" "catalog"). */}}
{{- define "souqly.selectorLabels" -}}
app.kubernetes.io/name: {{ .name }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
{{- end -}}

{{/* Réglages d'un service = defaults fusionnés avec le bloc du service. */}}
{{- define "souqly.serviceConfig" -}}
{{- $cfg := deepCopy .root.Values.defaults -}}
{{- $cfg = mergeOverwrite $cfg (deepCopy .svc) -}}
{{- toYaml $cfg -}}
{{- end -}}

{{/* Image d'un service. */}}
{{- define "souqly.image" -}}
{{- printf "%s/souqly-%s:%s" .root.Values.image.registry .name (default .root.Chart.AppVersion .root.Values.image.tag) -}}
{{- end -}}

{{/* Instance seule, pour les objets qui ne portent pas souqly.selectorLabels. */}}
{{- define "souqly.instanceLabel" -}}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}
