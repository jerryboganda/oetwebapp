import React from "react";
import { Container } from "reactstrap";
import Link from "next/link";
import { IconHome } from "@tabler/icons-react";

const MaintenancePage = () => {
  return (
    <div className="bg-light-primary maintenance py-5">
      <Container>
        <div className="overlay-maintenance-card text-center">
          <img
            alt="Maintenance Image"
            className="img-fluid"
            src="/images/pages/OBJECTS.png"
          />
          <h1 className="text-dark fw-semibold mt-4">
            We are <br />
            under Maintenance
          </h1>
          <p className="text-secondary">
            Someone has kidnapped our site. We are negotiating ransom and will
            resolve this issue in 24/7 hours
          </p>
          <Link
            href="/dashboard/project"
            className="btn btn-lg btn-light-danger mt-3 rounded-pill d-inline-flex align-items-center justify-content-center gap-1"
          >
            <IconHome size={18} />
            Back To Home
          </Link>
        </div>
      </Container>
    </div>
  );
};

export default MaintenancePage;
