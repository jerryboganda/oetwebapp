import React, { useState } from "react";
import { IconCircleFilled } from "@tabler/icons-react";
import Link from "next/link";
import { Tooltip } from "reactstrap";

const ProjectTable = () => {
  const [tooltipOpen, setTooltipOpen] = useState<{ [key: string]: boolean }>(
    {}
  );
  const toggleTooltip = (id: string) => {
    setTooltipOpen((prev) => ({
      ...prev,
      [id]: !prev[id],
    }));
  };

  const projectData = [
    {
      id: 1,
      projectName: "Web Redesign",
      status: "In Progress",
      statusClass: "text-light-warning",
      teamLead: {
        name: "Athena Stewart",
        avatar: "/images/avatar/2.png",
      },
      priority: "High",
      remarks: "Design phase completed",
    },
    {
      id: 2,
      projectName: "Web Redesign",
      status: "In Progress",
      statusClass: "text-light-warning",
      teamLead: {
        name: "Athena Stewart",
        avatar: "/images/avatar/2.png",
      },
      priority: "High",
      remarks: "Design phase completed",
    },
    {
      id: 3,
      projectName: "Mobile App",
      status: "Completed",
      statusClass: "text-light-success",
      teamLead: {
        name: "Jane Smith",
        avatar: "/images/avatar/3.png",
      },
      priority: "Medium",
      remarks: "Project deployed successfully",
    },
    {
      id: 4,
      projectName: "Campaign",
      status: "Not Started",
      statusClass: "text-light-secondary",
      teamLead: {
        name: "Mark Lee",
        avatar: "/images/avatar/4.png",
      },
      priority: "Low",
      remarks: "Campaign to begin in December",
    },
    {
      id: 5,
      projectName: "E-Commerce",
      status: "In Progress",
      statusClass: "text-light-warning",
      teamLead: {
        name: "Alice Johnson",
        avatar: "/images/avatar/5.png",
      },
      priority: "High",
      remarks: "Initial setup",
    },
    {
      id: 6,
      projectName: "Social Media",
      status: "Completed",
      statusClass: "text-light-success",
      teamLead: {
        name: "Sophia Green",
        avatar: "/images/avatar/4.png",
      },
      priority: "Low",
      remarks: "Campaign launched successfully",
    },
    {
      id: 7,
      projectName: "SEO Optimization",
      status: "In Progress",
      statusClass: "text-light-warning",
      teamLead: {
        name: "Liam Carter",
        avatar: "/images/avatar/5.png",
      },
      priority: "Medium",
      remarks: "Keyword analysis ongoing",
    },
    {
      id: 8,
      projectName: "UI/UX Revamp",
      status: "Scheduled",
      statusClass: "text-light-info",
      teamLead: {
        name: "Olivia Brown",
        avatar: "/images/avatar/6.png",
      },
      priority: "Low",
      remarks: "Resources allocated",
    },
  ];
  return (
    <div className="col-lg-7 col-xxl-6 order-1-md">
      <div className="p-3">
        <h5>Project Status</h5>
      </div>

      <div className="card mb-0">
        <div className="card-body py-2 px-0 overflow-hidden">
          <div className="table-responsive app-scroll ">
            <table className="table align-middle project-status-table mb-0">
              <thead>
                <tr>
                  <th scope="col">Project</th>
                  <th scope="col">Status</th>
                  <th scope="col">TeamLead</th>
                  <th scope="col">Priority</th>
                  <th scope="col">Remarks</th>
                </tr>
              </thead>
              <tbody>
                {projectData.map((project) => (
                  <tr key={project.id}>
                    <td>
                      <h6 className="mb-0 text-success-dark text-nowrap">
                        {project.projectName}
                      </h6>
                    </td>
                    <td>
                      <span
                        className={`badge ${project.statusClass} f-s-9 f-w-700`}
                      >
                        {project.status}
                      </span>
                    </td>
                    <td className="f-w-600 text-dark">
                      <Link
                        href="/"
                        className="h-30 w-30 d-flex-center b-r-50 overflow-hidden text-bg-secondary m-auto"
                        id={`tooltip-${project.id}`}
                      >
                        <img
                          alt="avatar"
                          className="img-fluid"
                          src={project.teamLead.avatar}
                        />
                        <Tooltip
                          target={`tooltip-${project.id}`}
                          isOpen={tooltipOpen[`tooltip-${project.id}`] || false}
                          toggle={() => toggleTooltip(`tooltip-${project.id}`)}
                        >
                          {project.teamLead.name}
                        </Tooltip>
                      </Link>
                    </td>
                    <td className="text-success-dark f-w-600">
                      {project.priority}
                    </td>
                    <td>
                      <span className="text-dark f-s-14 f-w-500 text-nowrap">
                        <IconCircleFilled size={7} className="me-2 f-s-6" />
                        {project.remarks}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>
      <div className="table-footer ">
        <p className="mb-0 f-s-15 f-w-500 txt-ellipsis-1">
          Showing 7 to 20 of 20 entries
        </p>
        <ul className="pagination app-pagination justify-content-end ">
          <li className="page-item disabled">
            <Link className="page-link b-r-left" href="#">
              Previous
            </Link>
          </li>
          <li className="page-item">
            <Link className="page-link" href="#">
              1
            </Link>
          </li>
          <li className="page-item active">
            <Link className="page-link" href="#">
              2
            </Link>
          </li>
          <li className="page-item">
            <Link className="page-link" href="#">
              3
            </Link>
          </li>
          <li className="page-item">
            <Link className="page-link" href="#">
              Next
            </Link>
          </li>
        </ul>
      </div>
    </div>
  );
};

export default ProjectTable;
