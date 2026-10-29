# Firebase (parent app)

`google-services.json` yahan **public Android app configuration** hai, project
`queen-school` ke liye. Yeh pehle se release APK mein shamil hai; is mein
service-account ki private key nahi honi chahiye. Android package:
`com.smartschool.smartschool_parent`. Google Cloud mein client API key ko
appropriate app restrictions dena chahiye.

Release workflow config copy karke `scripts/configure_parent_android.py` se
package/project verify karta hai aur Gradle plugin, notification permission aur
desugaring lagata hai. Config missing ya galat ho to release fail hogi. Optional
`PARENT_GOOGLE_SERVICES_JSON` Actions secret sirf same project ka Android client
config override karne ke liye hai — server ka service-account key kabhi APK ya
public Git repo mein na daalein.
