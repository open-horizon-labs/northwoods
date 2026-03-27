"""
Shared people roster for corpus generation.
~40 people across two tenants, long-tail visit distribution.

Distribution:
  - 25 people: 1 intake each
  - 8 people: 2-3 intakes (different form types)
  - 5 people: 4-6 intakes over 2-3 months
  - 2 people: 10+ intakes (frequent flyers)

Tenant assignment: first 20 to sunrise (tenant-a), last 20 to lakewood (tenant-b).
Frequent flyers: one per tenant.
"""

PEOPLE = [
    # id, first, last, dob, ssn_last4, address, city, state, zip, phone, tenant
    # --- Sunrise (tenant-a) ---
    {"id": "P001", "first": "Marcus", "last": "Delgado", "dob": "1985-03-12", "ssn4": "4821", "addr": "218 Birch St", "city": "Springfield", "state": "IL", "zip": "62701", "phone": "217-555-0112", "tenant": "sunrise", "visits": 1},
    {"id": "P002", "first": "Tamara", "last": "Okonkwo", "dob": "1992-07-28", "ssn4": "3307", "addr": "44 Elm Ave", "city": "Springfield", "state": "IL", "zip": "62702", "phone": "217-555-0234", "tenant": "sunrise", "visits": 1},
    {"id": "P003", "first": "Jesse", "last": "Huang", "dob": "1978-11-05", "ssn4": "9142", "addr": "730 Maple Dr", "city": "Springfield", "state": "IL", "zip": "62703", "phone": "217-555-0345", "tenant": "sunrise", "visits": 1},
    {"id": "P004", "first": "Brianna", "last": "Kowalski", "dob": "1999-01-17", "ssn4": "6674", "addr": "12 Oak Ln", "city": "Springfield", "state": "IL", "zip": "62704", "phone": "217-555-0456", "tenant": "sunrise", "visits": 1},
    {"id": "P005", "first": "Darnell", "last": "Foster", "dob": "1988-09-22", "ssn4": "2295", "addr": "88 Pine Ct", "city": "Springfield", "state": "IL", "zip": "62701", "phone": "217-555-0567", "tenant": "sunrise", "visits": 1},
    {"id": "P006", "first": "Yolanda", "last": "Reyes", "dob": "1975-04-03", "ssn4": "8813", "addr": "305 Cedar Blvd", "city": "Springfield", "state": "IL", "zip": "62702", "phone": "217-555-0678", "tenant": "sunrise", "visits": 1},
    {"id": "P007", "first": "Anton", "last": "Petrov", "dob": "1963-12-30", "ssn4": "5560", "addr": "77 Walnut Way", "city": "Springfield", "state": "IL", "zip": "62703", "phone": "217-555-0789", "tenant": "sunrise", "visits": 1},
    {"id": "P008", "first": "Keisha", "last": "Williams", "dob": "1995-06-14", "ssn4": "1147", "addr": "201 Spruce St", "city": "Springfield", "state": "IL", "zip": "62704", "phone": "217-555-0890", "tenant": "sunrise", "visits": 1},
    {"id": "P009", "first": "Rodrigo", "last": "Vega", "dob": "1982-08-09", "ssn4": "7732", "addr": "56 Ash Ave", "city": "Springfield", "state": "IL", "zip": "62701", "phone": "217-555-0901", "tenant": "sunrise", "visits": 1},
    {"id": "P010", "first": "Fatima", "last": "Al-Rashid", "dob": "1990-02-25", "ssn4": "3389", "addr": "143 Poplar Rd", "city": "Springfield", "state": "IL", "zip": "62702", "phone": "217-555-0102", "tenant": "sunrise", "visits": 1},
    {"id": "P011", "first": "Dennis", "last": "Achebe", "dob": "1971-10-18", "ssn4": "4456", "addr": "29 Hickory Pl", "city": "Springfield", "state": "IL", "zip": "62703", "phone": "217-555-0213", "tenant": "sunrise", "visits": 1},
    {"id": "P012", "first": "Luz", "last": "Morales", "dob": "2001-05-07", "ssn4": "6621", "addr": "87 Sycamore Dr", "city": "Springfield", "state": "IL", "zip": "62704", "phone": "217-555-0324", "tenant": "sunrise", "visits": 1},
    {"id": "P013", "first": "Trevor", "last": "Banks", "dob": "1967-03-29", "ssn4": "9987", "addr": "412 Willow Ct", "city": "Springfield", "state": "IL", "zip": "62701", "phone": "217-555-0435", "tenant": "sunrise", "visits": 1},
    # 2-3 visit people (sunrise)
    {"id": "P014", "first": "Nadia", "last": "Thornton", "dob": "1980-07-11", "ssn4": "2278", "addr": "66 Linden St", "city": "Springfield", "state": "IL", "zip": "62702", "phone": "217-555-0546", "tenant": "sunrise", "visits": 2},
    {"id": "P015", "first": "Elijah", "last": "Santos", "dob": "1993-11-02", "ssn4": "8845", "addr": "320 Beech Ave", "city": "Springfield", "state": "IL", "zip": "62703", "phone": "217-555-0657", "tenant": "sunrise", "visits": 3},
    {"id": "P016", "first": "Priya", "last": "Nair", "dob": "1987-04-16", "ssn4": "5512", "addr": "19 Magnolia Ln", "city": "Springfield", "state": "IL", "zip": "62704", "phone": "217-555-0768", "tenant": "sunrise", "visits": 2},
    # 4-6 visit people (sunrise)
    {"id": "P017", "first": "Carlton", "last": "Hughes", "dob": "1969-09-08", "ssn4": "1169", "addr": "534 Chestnut Blvd", "city": "Springfield", "state": "IL", "zip": "62701", "phone": "217-555-0879", "tenant": "sunrise", "visits": 5},
    {"id": "P018", "first": "Sonya", "last": "Blackwood", "dob": "1984-01-23", "ssn4": "7736", "addr": "78 Hazel Way", "city": "Springfield", "state": "IL", "zip": "62702", "phone": "217-555-0980", "tenant": "sunrise", "visits": 4},
    # Frequent flyer (sunrise)
    {"id": "P019", "first": "Raymond", "last": "Castillo", "dob": "1972-06-05", "ssn4": "3363", "addr": "902 Ironwood Dr", "city": "Springfield", "state": "IL", "zip": "62703", "phone": "217-555-1091", "tenant": "sunrise", "visits": 12},
    # One more 1-visit to round out sunrise
    {"id": "P020", "first": "Ingrid", "last": "Larsson", "dob": "1996-12-19", "ssn4": "4430", "addr": "45 Cottonwood Ct", "city": "Springfield", "state": "IL", "zip": "62704", "phone": "217-555-1102", "tenant": "sunrise", "visits": 1},

    # --- Lakewood (tenant-b) ---
    {"id": "P021", "first": "Theo", "last": "Mbeki", "dob": "1983-08-27", "ssn4": "5597", "addr": "117 River Rd", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-0112", "tenant": "lakewood", "visits": 1},
    {"id": "P022", "first": "Angela", "last": "Drummond", "dob": "1977-03-14", "ssn4": "8864", "addr": "283 Lake Shore Dr", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-0223", "tenant": "lakewood", "visits": 1},
    {"id": "P023", "first": "Victor", "last": "Pham", "dob": "1991-10-31", "ssn4": "2231", "addr": "56 Forest Ave", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-0334", "tenant": "lakewood", "visits": 1},
    {"id": "P024", "first": "Monique", "last": "Dupont", "dob": "1986-05-20", "ssn4": "7798", "addr": "189 Meadow Ln", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-0445", "tenant": "lakewood", "visits": 1},
    {"id": "P025", "first": "Hakeem", "last": "Osei", "dob": "1964-02-08", "ssn4": "4465", "addr": "74 Valley View Ct", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-0556", "tenant": "lakewood", "visits": 1},
    {"id": "P026", "first": "Camille", "last": "Bouchard", "dob": "1998-07-03", "ssn4": "1132", "addr": "331 Hillside Blvd", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-0667", "tenant": "lakewood", "visits": 1},
    {"id": "P027", "first": "Dmitri", "last": "Volkov", "dob": "1973-12-15", "ssn4": "6699", "addr": "28 Creekside Way", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-0778", "tenant": "lakewood", "visits": 1},
    {"id": "P028", "first": "Imani", "last": "Jefferson", "dob": "2000-04-22", "ssn4": "9966", "addr": "95 Sunset Rd", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-0889", "tenant": "lakewood", "visits": 1},
    {"id": "P029", "first": "Sergio", "last": "Gutierrez", "dob": "1979-09-17", "ssn4": "3333", "addr": "462 Prairie Dr", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-0990", "tenant": "lakewood", "visits": 1},
    {"id": "P030", "first": "Wanda", "last": "Korhonen", "dob": "1955-11-29", "ssn4": "7700", "addr": "13 Birchwood Ct", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-1001", "tenant": "lakewood", "visits": 1},
    {"id": "P031", "first": "Calvin", "last": "Pope", "dob": "1989-06-11", "ssn4": "4467", "addr": "207 Northfield Ave", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-1112", "tenant": "lakewood", "visits": 1},
    {"id": "P032", "first": "Esperanza", "last": "Cruz", "dob": "1994-01-04", "ssn4": "8834", "addr": "88 Southgate Blvd", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-1223", "tenant": "lakewood", "visits": 1},
    # 2-3 visit people (lakewood)
    {"id": "P033", "first": "Tobias", "last": "Stern", "dob": "1981-08-26", "ssn4": "2201", "addr": "51 Eastview Rd", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-1334", "tenant": "lakewood", "visits": 2},
    {"id": "P034", "first": "Alicia", "last": "Monroe", "dob": "1976-03-07", "ssn4": "5568", "addr": "174 Westwood Dr", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-1445", "tenant": "lakewood", "visits": 3},
    {"id": "P035", "first": "Patrick", "last": "Adeyemi", "dob": "1988-10-19", "ssn4": "9035", "addr": "39 Centerline Ct", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-1556", "tenant": "lakewood", "visits": 2},
    {"id": "P036", "first": "Giselle", "last": "Fontaine", "dob": "2003-05-25", "ssn4": "1102", "addr": "623 Ridgeview Ln", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-1667", "tenant": "lakewood", "visits": 2},
    # 4-6 visit people (lakewood)
    {"id": "P037", "first": "Bernard", "last": "Oduya", "dob": "1960-07-14", "ssn4": "6639", "addr": "290 Overlook Ave", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-1778", "tenant": "lakewood", "visits": 6},
    {"id": "P038", "first": "Tasha", "last": "Greenfield", "dob": "1997-02-28", "ssn4": "3306", "addr": "47 Timberline Blvd", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-1889", "tenant": "lakewood", "visits": 4},
    # Frequent flyer (lakewood)
    {"id": "P039", "first": "Gloria", "last": "Navarro", "dob": "1968-11-12", "ssn4": "7773", "addr": "815 Clearwater Dr", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-1990", "tenant": "lakewood", "visits": 11},
    # One more 1-visit
    {"id": "P040", "first": "Kofi", "last": "Asante", "dob": "1985-04-06", "ssn4": "4440", "addr": "122 Lakeview Ct", "city": "Lakewood", "state": "IL", "zip": "60014", "phone": "815-555-2001", "tenant": "lakewood", "visits": 1},
]

TENANT_IDS = {
    "sunrise": "tenant-a",
    "lakewood": "tenant-b",
}

FONT_NAMES = [
    "verdana",
    "arial",
    "trebuchet",
    "comic_sans",
    "bradley_hand",
    "marker_felt",
    "noteworthy",
    "chalkboard",
]

# Assign fill fonts per person: spread across all styles
import hashlib


def font_for_person(person_id):
    idx = int(hashlib.md5(person_id.encode()).hexdigest(), 16) % len(FONT_NAMES)
    return FONT_NAMES[idx], FONT_NAMES[idx]
